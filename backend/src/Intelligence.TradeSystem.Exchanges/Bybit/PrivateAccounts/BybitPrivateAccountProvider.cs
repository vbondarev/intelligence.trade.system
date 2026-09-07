using Bybit.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using Intelligence.TradeSystem.Exchanges.Bybit.Telemetry;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal sealed class BybitPrivateAccountProvider : IPrivateAccountProvider
{
    private readonly IBybitRestClient _client;
    private readonly ILogger<BybitPrivateAccountProvider> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;

    public BybitPrivateAccountProvider(
        IBybitRestClient client,
        ILogger<BybitPrivateAccountProvider> logger,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        _client = client;
        _logger = logger;
        _retryDelay = retryDelay ?? Task.Delay;
    }

    public async Task<OpenPositionsObservation> GetOpenPositionsAsync(MarketCategory category, string? symbol = null, CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException("Position data is not available for the Spot market. Use Linear or Inverse.", nameof(category));
        }

        using var activity = BybitExchangeTelemetry.StartActivity(BybitExchangeTelemetry.PositionsOperation);
        var stopwatch = Stopwatch.StartNew();
        var observedAt = DateTimeOffset.UtcNow;
        var positions = new List<OpenPosition>();
        string? cursor = null;
        var retryCount = 0;
        var outcome = BybitExchangeTelemetry.FailureOutcome;
        ExchangeFailure? failure = null;
        var cancelled = false;

        // Bybit paginates position lists via a cursor. A response is only a Complete snapshot
        // once every page has been fetched; an error mid-pagination can only ever downgrade to
        // Partial/Failed, never silently report an incomplete set as Complete.
        try
        {
            while (true)
            {
                var page = await ExecuteReadAsync(
                    ct => _client.V5Api.Trading.GetPositionsAsync(
                        category.ToBybitCategory(), symbol, null, null, 200, cursor, ct),
                    cancellationToken);
                retryCount += page.RetryCount;

                if (!page.Response.Success)
                {
                    failure = page.Failure;
                    outcome = positions.Count > 0
                        ? BybitExchangeTelemetry.PartialOutcome
                        : BybitExchangeTelemetry.FailureOutcome;
                    LogFailedPositions(category, symbol, failure, outcome, stopwatch.Elapsed);

                    return positions.Count > 0
                        ? OpenPositionsObservation.Partial(category, symbol, observedAt, positions, page.Response.Error?.Message)
                        : OpenPositionsObservation.Failed(
                            category,
                            symbol,
                            observedAt,
                            page.Response.Error?.Message ?? "Unknown Bybit API error.");
                }

                positions.AddRange(
                    page.Response.Data?.List?
                        .Where(position => position.Quantity > 0m)
                        .Select(position => position.MapOpenPosition(category))
                    ?? []);

                cursor = page.Response.Data?.NextPageCursor;
                if (string.IsNullOrEmpty(cursor))
                    break;
            }

            outcome = BybitExchangeTelemetry.SuccessOutcome;
            return OpenPositionsObservation.Complete(category, symbol, observedAt, positions);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        finally
        {
            if (cancelled)
            {
                outcome = BybitExchangeTelemetry.CancelledOutcome;
            }

            BybitExchangeTelemetry.Record(
                activity,
                BybitExchangeTelemetry.PositionsOperation,
                outcome,
                stopwatch.Elapsed,
                retryCount,
                failure,
                marketCategory: category);
        }
    }

    public async Task<AccountBalanceObservation> GetWalletBalanceAsync(AccountType accountType, CancellationToken cancellationToken = default)
    {
        using var activity = BybitExchangeTelemetry.StartActivity(BybitExchangeTelemetry.BalanceOperation);
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;
        var outcome = BybitExchangeTelemetry.FailureOutcome;
        ExchangeFailure? failure = null;
        var cancelled = false;

        try
        {
            var result = await ExecuteReadAsync(
                ct => _client.V5Api.Account.GetBalancesAsync(
                    accountType.ToBybitAccountType(), null, ct),
                cancellationToken);
            retryCount = result.RetryCount;

            if (!result.Response.Success)
            {
                failure = result.Failure
                    ?? new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false);
                LogFailedBalance(accountType, failure, stopwatch.Elapsed);
                return AccountBalanceObservation.Failed(failure);
            }

            if (result.Response.Data?.List?.FirstOrDefault() is not { } balance)
            {
                failure = new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false);
                LogFailedBalance(accountType, failure, stopwatch.Elapsed);
                return AccountBalanceObservation.Failed(failure);
            }

            outcome = BybitExchangeTelemetry.SuccessOutcome;
            return AccountBalanceObservation.Complete(balance.MapAccountBalance());
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            throw;
        }
        finally
        {
            if (cancelled)
            {
                outcome = BybitExchangeTelemetry.CancelledOutcome;
            }

            BybitExchangeTelemetry.Record(
                activity,
                BybitExchangeTelemetry.BalanceOperation,
                outcome,
                stopwatch.Elapsed,
                retryCount,
                failure,
                accountType: accountType);
        }
    }

    private async Task<ReadAttempt<T>> ExecuteReadAsync<T>(
        Func<CancellationToken, Task<HttpResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var response = await operation(cancellationToken).ConfigureAwait(false);
            if (response.Success)
            {
                return new ReadAttempt<T>(response, attempt - 1, null);
            }

            BybitExchangeFailureMapper.ThrowIfCancellationRequested(response.Error, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var failure = BybitExchangeFailureMapper.Map(response.Error);
            if (attempt >= BybitPrivateResiliencePolicy.MaxAttempts
                || !BybitPrivateResiliencePolicy.TryGetRetryDelay(failure, response.Error, out var delay))
            {
                return new ReadAttempt<T>(response, attempt - 1, failure);
            }

            await _retryDelay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private void LogFailedPositions(
        MarketCategory category,
        string? symbol,
        ExchangeFailure? failure,
        string outcome,
        TimeSpan elapsed)
    {
        var normalizedFailure = failure ?? new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false);
        BybitPrivateProviderLogMessages.LogFailedToFetchOpenPositions(
            _logger,
            BybitExchangeTelemetry.PositionsOperation,
            BybitExchangeTelemetry.ExchangeName,
            category,
            symbol ?? "all",
            normalizedFailure.Kind,
            normalizedFailure.Retryable,
            BybitExchangeFailureMapper.SafeProviderCode(normalizedFailure.ProviderCode),
            outcome,
            elapsed.TotalMilliseconds);
    }

    private void LogFailedBalance(AccountType accountType, ExchangeFailure failure, TimeSpan elapsed)
    {
        BybitPrivateProviderLogMessages.LogFailedToFetchWalletBalance(
            _logger,
            BybitExchangeTelemetry.BalanceOperation,
            BybitExchangeTelemetry.ExchangeName,
            accountType,
            failure.Kind,
            failure.Retryable,
            BybitExchangeFailureMapper.SafeProviderCode(failure.ProviderCode),
            BybitExchangeTelemetry.FailureOutcome,
            elapsed.TotalMilliseconds);
    }

    private readonly record struct ReadAttempt<T>(
        HttpResult<T> Response,
        int RetryCount,
        ExchangeFailure? Failure);
}
