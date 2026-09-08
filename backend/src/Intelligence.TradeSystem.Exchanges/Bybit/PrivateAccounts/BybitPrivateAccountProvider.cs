using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Accounts.Access;
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
    private static readonly string[] SupportedLinearSettlementCoins = ["USDT", "USDC"];

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

    public async Task<OpenPositionsObservation> GetOpenPositionsAsync(
        MarketCategory category,
        string? symbol = null,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException(
                "Position data is not available for the Spot market. Use Linear or Inverse.",
                nameof(category));
        }

        using var activity = BybitExchangeTelemetry.StartActivity(BybitExchangeTelemetry.PositionsOperation);
        var stopwatch = Stopwatch.StartNew();
        var observedAt = DateTimeOffset.UtcNow;
        var outcome = BybitExchangeTelemetry.FailureOutcome;
        var retryCount = 0;
        ExchangeFailure? retryFailure = null;
        ExchangeFailure? failure = null;
        var cancelled = false;

        try
        {
            IReadOnlyList<PositionScopeResult> scopes;
            if (category == MarketCategory.Linear && symbol is null)
            {
                var settlementScopes = new PositionScopeResult[SupportedLinearSettlementCoins.Length];
                for (var index = 0; index < SupportedLinearSettlementCoins.Length; index++)
                {
                    settlementScopes[index] = await FetchPositionScopeAsync(
                        category,
                        symbol,
                        SupportedLinearSettlementCoins[index],
                        observedAt,
                        stopwatch,
                        cancellationToken).ConfigureAwait(false);
                }

                scopes = settlementScopes;
            }
            else
            {
                scopes =
                [
                    await FetchPositionScopeAsync(
                        category,
                        symbol,
                        settleCoin: null,
                        observedAt,
                        stopwatch,
                        cancellationToken).ConfigureAwait(false),
                ];
            }

            foreach (var scope in scopes)
            {
                retryCount += scope.RetryCount;
                retryFailure = scope.RetryFailure ?? retryFailure;
                failure = scope.Failure ?? failure;
            }

            var aggregate = AggregatePositionScopes(category, symbol, observedAt, scopes);
            outcome = aggregate.Status switch
            {
                OpenPositionsObservationStatus.Complete => BybitExchangeTelemetry.SuccessOutcome,
                OpenPositionsObservationStatus.Partial => BybitExchangeTelemetry.PartialOutcome,
                _ => BybitExchangeTelemetry.FailureOutcome,
            };
            return aggregate;
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
                retryFailure,
                marketCategory: category);
        }
    }

    private async Task<PositionScopeResult> FetchPositionScopeAsync(
        MarketCategory category,
        string? symbol,
        string? settleCoin,
        DateTimeOffset observedAt,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var positions = new List<OpenPosition>();
        string? cursor = null;
        var retryCount = 0;
        ExchangeFailure? retryFailure = null;

        while (true)
        {
            var page = await ExecuteReadAsync(
                ct => _client.V5Api.Trading.GetPositionsAsync(
                    category.ToBybitCategory(),
                    symbol,
                    null,
                    settleCoin,
                    200,
                    cursor,
                    ct),
                cancellationToken).ConfigureAwait(false);
            retryCount += page.RetryCount;
            retryFailure = page.RetryFailure ?? retryFailure;

            if (!page.Response.Success)
            {
                var failure = page.Failure;
                var outcome = positions.Count > 0
                    ? BybitExchangeTelemetry.PartialOutcome
                    : BybitExchangeTelemetry.FailureOutcome;
                LogFailedPositions(category, symbol, failure, outcome, stopwatch.Elapsed);

                var observation = positions.Count > 0
                    ? OpenPositionsObservation.Partial(
                        category,
                        symbol,
                        observedAt,
                        positions,
                        page.Response.Error?.Message)
                    : OpenPositionsObservation.Failed(
                        category,
                        symbol,
                        observedAt,
                        page.Response.Error?.Message ?? "Unknown Bybit API error.");
                return new PositionScopeResult(
                    observation,
                    retryCount,
                    retryFailure,
                    failure);
            }

            positions.AddRange(
                page.Response.Data?.List?
                    .Where(position => position.Quantity > 0m)
                    .Select(position => position.MapOpenPosition(category))
                ?? []);

            cursor = page.Response.Data?.NextPageCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                return new PositionScopeResult(
                    OpenPositionsObservation.Complete(category, symbol, observedAt, positions),
                    retryCount,
                    retryFailure,
                    null);
            }
        }
    }

    private static OpenPositionsObservation AggregatePositionScopes(
        MarketCategory category,
        string? symbol,
        DateTimeOffset observedAt,
        IReadOnlyList<PositionScopeResult> scopes)
    {
        var positions = scopes
            .SelectMany(scope => scope.Observation.Positions)
            .ToArray();
        var hasPartial = scopes.Any(scope =>
            scope.Observation.Status == OpenPositionsObservationStatus.Partial);
        var hasFailed = scopes.Any(scope =>
            scope.Observation.Status == OpenPositionsObservationStatus.Failed);
        var error = scopes
            .Select(scope => scope.Observation.Error)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!hasPartial && !hasFailed)
        {
            return OpenPositionsObservation.Complete(category, symbol, observedAt, positions);
        }

        if (scopes.All(scope => scope.Observation.Status == OpenPositionsObservationStatus.Failed))
        {
            return OpenPositionsObservation.Failed(
                category,
                symbol,
                observedAt,
                error ?? "All Bybit position scopes failed.");
        }

        return OpenPositionsObservation.Partial(
            category,
            symbol,
            observedAt,
            positions,
            error);
    }

    public async Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
        CancellationToken cancellationToken = default)
    {
        using var activity = BybitExchangeTelemetry.StartActivity(BybitExchangeTelemetry.ApiKeyAccessOperation);
        var stopwatch = Stopwatch.StartNew();
        var retryCount = 0;
        ExchangeFailure? retryFailure = null;
        var outcome = BybitExchangeTelemetry.FailureOutcome;
        ExchangeFailure? failure = null;
        var cancelled = false;

        try
        {
            var result = await ExecuteReadAsync(
                ct => _client.V5Api.Account.GetApiKeyInfoAsync(ct),
                cancellationToken);
            retryCount = result.RetryCount;
            retryFailure = result.RetryFailure;

            if (!result.Response.Success)
            {
                failure = result.Failure
                    ?? new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false);
                BybitPrivateProviderLogMessages.LogFailedToFetchApiKeyAccess(
                    _logger,
                    BybitExchangeTelemetry.ApiKeyAccessOperation,
                    BybitExchangeTelemetry.ExchangeName,
                    failure.Kind,
                    failure.Retryable,
                    BybitExchangeFailureMapper.SafeProviderCode(failure.ProviderCode),
                    BybitExchangeTelemetry.FailureOutcome,
                    stopwatch.Elapsed.TotalMilliseconds);
                return ApiKeyAccessMetadataObservation.Failed(failure);
            }

            if (result.Response.Data is not { } apiKeyInfo)
            {
                failure = new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false);
                BybitPrivateProviderLogMessages.LogFailedToFetchApiKeyAccess(
                    _logger,
                    BybitExchangeTelemetry.ApiKeyAccessOperation,
                    BybitExchangeTelemetry.ExchangeName,
                    failure.Kind,
                    failure.Retryable,
                    null,
                    BybitExchangeTelemetry.FailureOutcome,
                    stopwatch.Elapsed.TotalMilliseconds);
                return ApiKeyAccessMetadataObservation.Failed(failure);
            }

            outcome = BybitExchangeTelemetry.SuccessOutcome;
            return ApiKeyAccessMetadataObservation.Complete(new ApiKeyAccessMetadata(apiKeyInfo.Readonly));
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
                BybitExchangeTelemetry.ApiKeyAccessOperation,
                outcome,
                stopwatch.Elapsed,
                retryCount,
                failure,
                retryFailure);
        }
    }

    public async Task<AccountBalanceObservation> GetWalletBalanceAsync(AccountType accountType, CancellationToken cancellationToken = default)
    {
        using var activity = BybitExchangeTelemetry.StartActivity(BybitExchangeTelemetry.BalanceOperation);
        var stopwatch = Stopwatch.StartNew();
        var observedAt = DateTimeOffset.UtcNow;
        var retryCount = 0;
        ExchangeFailure? retryFailure = null;
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
            retryFailure = result.RetryFailure;

            if (!result.Response.Success)
            {
                failure = result.Failure
                    ?? new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false);
                LogFailedBalance(accountType, failure, stopwatch.Elapsed);
                return AccountBalanceObservation.Failed(failure, observedAt);
            }

            if (result.Response.Data?.List?.FirstOrDefault() is not { } balance)
            {
                failure = new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false);
                LogFailedBalance(accountType, failure, stopwatch.Elapsed);
                return AccountBalanceObservation.Failed(failure, observedAt);
            }

            outcome = BybitExchangeTelemetry.SuccessOutcome;
            return AccountBalanceObservation.Complete(balance.MapAccountBalance(), observedAt);
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
                retryFailure,
                accountType: accountType);
        }
    }

    private async Task<ReadAttempt<T>> ExecuteReadAsync<T>(
        Func<CancellationToken, Task<HttpResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        ExchangeFailure? retryFailure = null;

        for (var attempt = 1; ; attempt++)
        {
            var response = await operation(cancellationToken).ConfigureAwait(false);
            if (response.Success)
            {
                return new ReadAttempt<T>(response, attempt - 1, retryFailure, null);
            }

            BybitExchangeFailureMapper.ThrowIfCancellationRequested(response.Error, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var failure = BybitExchangeFailureMapper.Map(response.Error);
            if (attempt >= BybitPrivateResiliencePolicy.MaxAttempts
                || !BybitPrivateResiliencePolicy.TryGetRetryDelay(failure, response.Error, out var delay))
            {
                return new ReadAttempt<T>(response, attempt - 1, retryFailure, failure);
            }

            retryFailure = failure;
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
        ExchangeFailure? RetryFailure,
        ExchangeFailure? Failure);

    private readonly record struct PositionScopeResult(
        OpenPositionsObservation Observation,
        int RetryCount,
        ExchangeFailure? RetryFailure,
        ExchangeFailure? Failure);
}
