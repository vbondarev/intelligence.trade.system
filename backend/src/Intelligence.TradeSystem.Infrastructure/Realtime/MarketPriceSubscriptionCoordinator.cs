using System.Collections.Concurrent;
using System.Collections.Generic;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

public sealed partial class MarketPriceSubscriptionCoordinator(
    IServiceScopeFactory scopeFactory,
    IMarketMarkPriceSubscriptionProvider provider,
    TimeProvider timeProvider,
    ILogger<MarketPriceSubscriptionCoordinator> logger)
    : BackgroundService
{
    private static readonly TimeSpan SubscriptionRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PersistencePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PersistenceInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, SubscriptionState> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingMarkPrice> _latestPrices =
        new(StringComparer.Ordinal);
    private long _nextGeneration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PersistencePollInterval, timeProvider);
        var hasRefreshedSubscriptions = false;
        var lastRefreshTimestamp = 0L;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = timeProvider.GetTimestamp();
                if (!hasRefreshedSubscriptions
                    || timeProvider.GetElapsedTime(lastRefreshTimestamp, now) >= SubscriptionRefreshInterval)
                {
                    hasRefreshedSubscriptions = true;
                    lastRefreshTimestamp = now;
                    try
                    {
                        await RefreshSubscriptionsAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        LogSubscriptionDiscoveryFailed(logger, exception.GetType().Name);
                    }
                }

                try
                {
                    await PersistDuePricesAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogPersistenceFailed(logger, exception.GetType().Name);
                }

                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await StopSubscriptionsAsync().ConfigureAwait(false);
        }
    }

    private async Task RefreshSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var desiredSymbols = await ReadSupportedActiveSymbolsAsync(cancellationToken).ConfigureAwait(false);
        var desired = desiredSymbols.ToHashSet(StringComparer.Ordinal);

        foreach (var symbol in _subscriptions.Keys
                     .Where(symbol => !desired.Contains(symbol))
                     .ToArray())
        {
            var subscription = _subscriptions[symbol];
            _subscriptions.Remove(symbol);
            await DisposeSubscriptionAsync(symbol, subscription).ConfigureAwait(false);
        }

        foreach (var symbol in desiredSymbols)
        {
            if (_subscriptions.TryGetValue(symbol, out var existing)
                && Volatile.Read(ref existing.IsActive) != 0)
            {
                continue;
            }

            if (existing is not null)
            {
                _subscriptions.Remove(symbol);
                await DisposeSubscriptionAsync(symbol, existing).ConfigureAwait(false);
            }

            var state = new SubscriptionState(Interlocked.Increment(ref _nextGeneration));
            _subscriptions.Add(symbol, state);

            try
            {
                state.Lease = await provider.SubscribeAsync(
                    InstrumentId.From(symbol),
                    observation => Observe(state, observation),
                    () => Interlocked.Exchange(ref state.IsActive, 0),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _subscriptions.Remove(symbol);
                await DisposeSubscriptionAsync(symbol, state).ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                _subscriptions.Remove(symbol);
                await DisposeSubscriptionAsync(symbol, state).ConfigureAwait(false);
                LogSubscriptionFailed(logger, exception.GetType().Name);
            }
        }
    }

    private async Task<IReadOnlyList<string>> ReadSupportedActiveSymbolsAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradeSystemDbContext>();
        return await dbContext.Positions
            .AsNoTracking()
            .Where(position =>
                position.MarketCategory == MarketCategory.Linear
                && position.TrackingState != PositionTrackingState.Closed
                && position.Size > 0m
                && (position.SettlementAsset == "USDT" || position.SettlementAsset == "USDC")
                && dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId
                    && account.ExchangeId == ExchangeId.Bybit
                    && (account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected
                        || account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable)))
            .Select(position => position.InstrumentId)
            .Distinct()
            .OrderBy(symbol => symbol)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private void Observe(SubscriptionState state, MarketMarkPriceObservation observation)
    {
        if (Volatile.Read(ref state.IsActive) == 0
            || observation.Exchange != ExchangeId.Bybit
            || observation.Category != MarketCategory.Linear
            || observation.MarkPrice is not > 0m
            || observation.ObservedAt is not { } observedAt)
        {
            return;
        }

        var symbol = InstrumentId.From(observation.Symbol);
        var price = new MarketMarkPrice
        {
            Exchange = observation.Exchange,
            Category = observation.Category,
            Symbol = symbol,
            MarkPrice = observation.MarkPrice.Value,
            ObservedAt = observedAt,
            ReceivedAt = observation.ReceivedAt,
            Source = MarketMarkPriceSource.PublicTicker,
        };
        var key = symbol.Value;
        var candidate = new PendingMarkPrice(state.Generation, price);

        while (Volatile.Read(ref state.IsActive) != 0)
        {
            if (!_latestPrices.TryGetValue(key, out var current))
            {
                if (_latestPrices.TryAdd(key, candidate))
                {
                    break;
                }

                continue;
            }

            if (current.Generation != state.Generation || current.Price.ObservedAt >= observedAt)
            {
                return;
            }

            if (_latestPrices.TryUpdate(key, candidate, current))
            {
                break;
            }
        }

        if (Volatile.Read(ref state.IsActive) == 0)
        {
            RemovePendingPrice(key, state.Generation);
        }
    }

    private async Task PersistDuePricesAsync(CancellationToken cancellationToken)
    {
        var timestamp = timeProvider.GetTimestamp();
        var due = new List<(SubscriptionState State, MarketMarkPrice Price)>();
        foreach (var pair in _subscriptions)
        {
            var state = pair.Value;
            if (Volatile.Read(ref state.IsActive) == 0
                || !_latestPrices.TryGetValue(pair.Key, out var pending)
                || pending.Generation != state.Generation
                || state.LastPersistedObservedAt is { } persistedAt
                    && pending.Price.ObservedAt <= persistedAt
                || state.LastPersistAttemptTimestamp is { } lastAttempt
                    && timeProvider.GetElapsedTime(lastAttempt, timestamp) < PersistenceInterval)
            {
                continue;
            }

            state.LastPersistAttemptTimestamp = timestamp;
            due.Add((state, pending.Price));
        }

        if (due.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMarketMarkPriceStore>();
        await store.UpsertAsync(
            due.Select(item => item.Price).ToArray(),
            cancellationToken).ConfigureAwait(false);

        foreach (var (state, price) in due)
        {
            if (state.LastPersistedObservedAt is not { } persistedAt
                || price.ObservedAt > persistedAt)
            {
                state.LastPersistedObservedAt = price.ObservedAt;
            }
        }
    }

    private async Task StopSubscriptionsAsync()
    {
        foreach (var (symbol, state) in _subscriptions.ToArray())
        {
            _subscriptions.Remove(symbol);
            await DisposeSubscriptionAsync(symbol, state).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeSubscriptionAsync(string symbol, SubscriptionState state)
    {
        Volatile.Write(ref state.IsActive, 0);
        RemovePendingPrice(symbol, state.Generation);
        if (state.Lease is not { } lease)
        {
            return;
        }

        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogSubscriptionDisposeFailed(logger, exception.GetType().Name);
        }
    }

    private void RemovePendingPrice(string symbol, long generation)
    {
        if (_latestPrices.TryGetValue(symbol, out var current)
            && current.Generation == generation)
        {
            ((ICollection<KeyValuePair<string, PendingMarkPrice>>)_latestPrices)
                .Remove(new KeyValuePair<string, PendingMarkPrice>(symbol, current));
        }
    }

    private sealed class SubscriptionState(long generation)
    {
        public long Generation { get; } = generation;
        public int IsActive = 1;
        public IAsyncDisposable? Lease { get; set; }
        public DateTimeOffset? LastPersistedObservedAt { get; set; }
        public long? LastPersistAttemptTimestamp { get; set; }
    }

    private sealed record PendingMarkPrice(long Generation, MarketMarkPrice Price);

    [LoggerMessage(
        EventId = 1220,
        Level = LogLevel.Error,
        Message = "Не удалось обновить список инструментов для публичных рыночных подписок. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionDiscoveryFailed(
        ILogger logger,
        string exceptionType);

    [LoggerMessage(
        EventId = 1221,
        Level = LogLevel.Error,
        Message = "Не удалось сохранить публичные mark price. ExceptionType={ExceptionType}")]
    private static partial void LogPersistenceFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 1222,
        Level = LogLevel.Warning,
        Message = "Не удалось подписаться на публичный mark price; будет выполнена повторная попытка. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 1223,
        Level = LogLevel.Warning,
        Message = "Не удалось закрыть публичную рыночную подписку. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionDisposeFailed(
        ILogger logger,
        string exceptionType);
}
