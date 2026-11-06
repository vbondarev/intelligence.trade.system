using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static readonly TimeSpan SubscriptionAttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShutdownCleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, SubscriptionState> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingMarkPrice> _latestPrices =
        new(StringComparer.Ordinal);
    private readonly List<Task> _lateCleanupTasks = [];
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
                ProcessSubscriptionOperations();
                var now = timeProvider.GetTimestamp();
                if (!hasRefreshedSubscriptions
                    || timeProvider.GetElapsedTime(lastRefreshTimestamp, now) >= SubscriptionRefreshInterval)
                {
                    hasRefreshedSubscriptions = true;
                    lastRefreshTimestamp = now;
                    try
                    {
                        await RefreshSubscriptionsAsync(stoppingToken).ConfigureAwait(false);
                        ProcessSubscriptionOperations();
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

        foreach (var (symbol, state) in _subscriptions.ToArray())
        {
            if (!desired.Contains(symbol))
            {
                BeginStopping(state);
            }
        }

        foreach (var symbol in desiredSymbols)
        {
            if (_subscriptions.ContainsKey(symbol))
            {
                continue;
            }

            var state = new SubscriptionState(
                symbol,
                Interlocked.Increment(ref _nextGeneration));
            StartSubscription(state, cancellationToken);
        }
    }

    private void StartSubscription(SubscriptionState state, CancellationToken stoppingToken)
    {
        _subscriptions.Add(state.Symbol, state);
        state.StartTimestamp = Stopwatch.GetTimestamp();
        var timeout = new CancellationTokenSource(SubscriptionAttemptTimeout);
        state.AttemptTimeout = timeout;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            timeout.Token);
        state.Cancellation = cancellation;

        try
        {
            state.SubscribeTask = provider.SubscribeAsync(
                    InstrumentId.From(state.Symbol),
                    observation => Observe(state, observation),
                    () => SignalSubscriptionFailure(state),
                    cancellation.Token)
                ?? throw new InvalidOperationException("Subscription provider returned no operation.");
            state.TimeoutRegistration = timeout.Token.Register(
                static value =>
                {
                    var subscription = (SubscriptionState)value!;
                    if (Volatile.Read(ref subscription.AttemptCompletionObserved) == 0
                        && subscription.SubscribeTask is { IsCompleted: false })
                    {
                        Interlocked.Exchange(ref subscription.TimedOut, 1);
                    }
                },
                state);
            if (Stopwatch.GetElapsedTime(state.StartTimestamp) >= SubscriptionAttemptTimeout)
            {
                Interlocked.Exchange(ref state.TimedOut, 1);
            }
        }
        catch (Exception exception)
        {
            if (exception is MarketPriceSubscriptionStartUnconfirmedException unconfirmedException)
            {
                MarkStartUnconfirmed(state, unconfirmedException);
                return;
            }

            state.Lifecycle = SubscriptionLifecycle.Failed;
            Deactivate(state);
            RemovePendingPrice(state.Symbol, state.Generation);
            LogSubscriptionFailed(logger, exception.GetType().Name);
            DisposeCancellation(state);
            RemoveSubscriptionState(state);
        }
    }

    private void ProcessSubscriptionOperations()
    {
        foreach (var state in _subscriptions.Values.ToArray())
        {
            if (state.Lifecycle == SubscriptionLifecycle.Active
                && Volatile.Read(ref state.FailureRequested) != 0)
            {
                BeginStopping(state);
            }
            else if (state.Lifecycle == SubscriptionLifecycle.Starting
                     && (Volatile.Read(ref state.TimedOut) != 0
                         || Volatile.Read(ref state.FailureRequested) != 0))
            {
                BeginStopping(state);
            }

            if (state.SubscribeTask is { IsCompleted: true })
            {
                CompleteSubscriptionAttempt(state);
            }

            if (state.Lifecycle != SubscriptionLifecycle.Stopping
                || state.SubscribeTask is not null)
            {
                continue;
            }

            if (state.Lease is not null
                && state.DisposeTask is null
                && !state.DisposeUnconfirmed
                && !state.StartUnconfirmed)
            {
                StartLeaseDisposal(state);
            }

            if (state.DisposeTask is { IsCompleted: true })
            {
                CompleteLeaseDisposal(state);
            }
            else if (state.Lease is null
                     && state.DisposeTask is null
                     && !state.StartUnconfirmed)
            {
                DisposeCancellation(state);
                RemoveSubscriptionState(state);
            }
        }
    }

    private void CompleteSubscriptionAttempt(SubscriptionState state)
    {
        var subscribeTask = state.SubscribeTask!;
        Interlocked.Exchange(ref state.AttemptCompletionObserved, 1);
        state.SubscribeTask = null;

        try
        {
            state.Lease = subscribeTask.GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Subscription provider returned no lease.");
            if (state.Lifecycle == SubscriptionLifecycle.Starting
                && Volatile.Read(ref state.TimedOut) == 0
                && Volatile.Read(ref state.FailureRequested) == 0)
            {
                DisposeAttemptTimeout(state);
                state.Lifecycle = SubscriptionLifecycle.Active;
                return;
            }

            state.Lifecycle = SubscriptionLifecycle.Stopping;
            Deactivate(state);
            RemovePendingPrice(state.Symbol, state.Generation);
            StartLeaseDisposal(state);
        }
        catch (MarketPriceSubscriptionStartUnconfirmedException exception)
        {
            MarkStartUnconfirmed(state, exception);
        }
        catch (Exception exception)
        {
            state.Lifecycle = SubscriptionLifecycle.Failed;
            Deactivate(state);
            RemovePendingPrice(state.Symbol, state.Generation);
            LogSubscriptionFailed(logger, exception.GetType().Name);
            DisposeCancellation(state);
            RemoveSubscriptionState(state);
        }
    }

    private void MarkStartUnconfirmed(
        SubscriptionState state,
        MarketPriceSubscriptionStartUnconfirmedException exception)
    {
        var alreadyProtected = state.StartUnconfirmed || state.DisposeUnconfirmed;
        state.Lifecycle = SubscriptionLifecycle.Stopping;
        Deactivate(state);
        RemovePendingPrice(state.Symbol, state.Generation);
        if (!state.StartUnconfirmed)
        {
            state.StartUnconfirmed = true;
            ExchangeRealtimeObservationTelemetry.RecordPublicSubscriptionStartUnconfirmed();
        }

        if (exception.UnconfirmedLease is { } lease)
        {
            state.Lease = lease;
            MarkDisposeUnconfirmed(state);
        }

        if (!alreadyProtected)
        {
            ExchangeRealtimeObservationTelemetry.RecordProtectedPublicSubscription();
        }

        LogSubscriptionStartUnconfirmed(logger, exception.GetType().Name);
        DisposeCancellation(state);
    }

    private void BeginStopping(SubscriptionState state)
    {
        if (state.Lifecycle is SubscriptionLifecycle.Stopping or SubscriptionLifecycle.Failed)
        {
            return;
        }

        state.Lifecycle = SubscriptionLifecycle.Stopping;
        Deactivate(state);
        RemovePendingPrice(state.Symbol, state.Generation);
        try
        {
            state.Cancellation?.Cancel();
        }
        catch (Exception exception)
        {
            LogSubscriptionCancellationFailed(logger, exception.GetType().Name);
        }

        if (state.SubscribeTask is null && state.Lease is not null)
        {
            StartLeaseDisposal(state);
        }
    }

    private void StartLeaseDisposal(SubscriptionState state)
    {
        if (state.Lease is not { } lease || state.DisposeTask is not null || state.DisposeUnconfirmed)
        {
            return;
        }

        try
        {
            state.DisposeTask = lease.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            MarkDisposeUnconfirmed(state);
            LogSubscriptionDisposeFailed(logger, exception.GetType().Name);
        }
    }

    private void CompleteLeaseDisposal(SubscriptionState state)
    {
        var disposeTask = state.DisposeTask!;
        state.DisposeTask = null;
        try
        {
            disposeTask.GetAwaiter().GetResult();
            state.Lease = null;
            DisposeCancellation(state);
            RemoveSubscriptionState(state);
        }
        catch (Exception exception)
        {
            MarkDisposeUnconfirmed(state);
            LogSubscriptionDisposeFailed(logger, exception.GetType().Name);
        }
    }

    private static void SignalSubscriptionFailure(SubscriptionState state)
    {
        Interlocked.Exchange(ref state.FailureRequested, 1);
        Deactivate(state);
    }

    private static void Deactivate(SubscriptionState state) =>
        Interlocked.Exchange(ref state.IsActive, 0);

    private static void DisposeAttemptTimeout(SubscriptionState state)
    {
        state.TimeoutRegistration.Dispose();
        state.AttemptTimeout?.Dispose();
        state.AttemptTimeout = null;
    }

    private static void DisposeCancellation(SubscriptionState state)
    {
        state.Cancellation?.Dispose();
        state.Cancellation = null;
        DisposeAttemptTimeout(state);
    }

    private void RemoveSubscriptionState(SubscriptionState state)
    {
        if (_subscriptions.TryGetValue(state.Symbol, out var current)
            && ReferenceEquals(current, state))
        {
            _subscriptions.Remove(state.Symbol);
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
            || !string.Equals(observation.Symbol, state.Symbol, StringComparison.Ordinal)
            || observation.MarkPrice is not > 0m
            || observation.ObservedAt is not { } observedAt)
        {
            return;
        }

        var symbol = InstrumentId.From(state.Symbol);
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
        var key = state.Symbol;
        var candidate = new PendingMarkPrice(state.Generation, price);

        TryStorePendingPrice(_latestPrices, state, candidate);

        if (Volatile.Read(ref state.IsActive) == 0)
        {
            RemovePendingPrice(key, state.Generation);
        }
    }

    internal static bool TryStorePendingPrice(
        ConcurrentDictionary<string, PendingMarkPrice> latestPrices,
        SubscriptionState state,
        PendingMarkPrice candidate)
    {
        while (Volatile.Read(ref state.IsActive) != 0)
        {
            if (!latestPrices.TryGetValue(state.Symbol, out var current))
            {
                if (latestPrices.TryAdd(state.Symbol, candidate))
                {
                    return true;
                }

                continue;
            }

            if (current.Generation > candidate.Generation
                || (current.Generation == candidate.Generation
                    && current.Price.ObservedAt >= candidate.Price.ObservedAt))
            {
                return false;
            }

            if (latestPrices.TryUpdate(state.Symbol, candidate, current))
            {
                return true;
            }
        }

        return false;
    }

    private async Task PersistDuePricesAsync(CancellationToken cancellationToken)
    {
        var timestamp = timeProvider.GetTimestamp();
        var due = new List<(SubscriptionState State, MarketMarkPrice Price)>();
        foreach (var pair in _subscriptions)
        {
            var state = pair.Value;
            if (state.Lifecycle != SubscriptionLifecycle.Active
                || Volatile.Read(ref state.IsActive) == 0
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
        foreach (var state in _subscriptions.Values.ToArray())
        {
            BeginStopping(state);
        }

        var shutdownTimer = Stopwatch.StartNew();
        while (shutdownTimer.Elapsed < ShutdownCleanupTimeout
               && HasPendingSubscriptionOperations())
        {
            ProcessSubscriptionOperations();
            if (HasPendingSubscriptionOperations())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
            }
        }

        ProcessSubscriptionOperations();
        foreach (var state in _subscriptions.Values.ToArray())
        {
            if (state.SubscribeTask is { } subscribeTask)
            {
                state.SubscribeTask = null;
                _lateCleanupTasks.Add(DisposeLateSubscriptionAsync(state, subscribeTask));
                continue;
            }

            if (state.DisposeTask is { } disposeTask)
            {
                state.DisposeTask = null;
                MarkDisposeUnconfirmed(state);
                LogSubscriptionDisposePendingAtShutdown(logger);
                _lateCleanupTasks.Add(ObserveLateDisposalAsync(state, disposeTask));
                continue;
            }

            if (!state.DisposeUnconfirmed && !state.StartUnconfirmed)
            {
                DisposeCancellation(state);
                RemoveSubscriptionState(state);
            }
        }
    }

    private bool HasPendingSubscriptionOperations() =>
        _subscriptions.Values.Any(state =>
            state.SubscribeTask is { IsCompleted: false }
            || state.DisposeTask is { IsCompleted: false });

    private async Task DisposeLateSubscriptionAsync(
        SubscriptionState state,
        Task<IAsyncDisposable> subscribeTask)
    {
        IAsyncDisposable lease;
        try
        {
            lease = await subscribeTask.ConfigureAwait(false)
                ?? throw new InvalidOperationException("Subscription provider returned no lease.");
        }
        catch (Exception exception)
        {
            if (exception is MarketPriceSubscriptionStartUnconfirmedException unconfirmedException)
            {
                MarkStartUnconfirmed(state, unconfirmedException);
                return;
            }

            LogSubscriptionFailed(logger, exception.GetType().Name);
            DisposeCancellation(state);
            RemoveSubscriptionState(state);
            return;
        }

        state.Lease = lease;
        try
        {
            await lease.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            MarkDisposeUnconfirmed(state);
            LogSubscriptionDisposeFailed(logger, exception.GetType().Name);
            return;
        }

        state.Lease = null;
        DisposeCancellation(state);
        RemoveSubscriptionState(state);
    }

    private async Task ObserveLateDisposalAsync(SubscriptionState state, Task disposeTask)
    {
        try
        {
            await disposeTask.ConfigureAwait(false);
            var wasProtected = state.StartUnconfirmed || state.DisposeUnconfirmed;
            state.DisposeTask = null;
            state.Lease = null;
            state.DisposeUnconfirmed = false;
            if (wasProtected && !state.StartUnconfirmed)
            {
                ExchangeRealtimeObservationTelemetry.RecordProtectedPublicSubscriptionReleased();
            }

            DisposeCancellation(state);
            RemoveSubscriptionState(state);
        }
        catch (Exception exception)
        {
            MarkDisposeUnconfirmed(state);
            LogSubscriptionDisposeFailed(logger, exception.GetType().Name);
        }
    }

    private static void MarkDisposeUnconfirmed(SubscriptionState state)
    {
        var alreadyProtected = state.StartUnconfirmed || state.DisposeUnconfirmed;
        if (!state.DisposeUnconfirmed)
        {
            state.DisposeUnconfirmed = true;
            ExchangeRealtimeObservationTelemetry.RecordPublicSubscriptionDisposeUnconfirmed();
        }

        if (!alreadyProtected)
        {
            ExchangeRealtimeObservationTelemetry.RecordProtectedPublicSubscription();
        }
    }

    private void RemovePendingPrice(string symbol, long generation)
    {
        RemovePendingPrice(_latestPrices, symbol, generation);
    }

    internal static void RemovePendingPrice(
        ConcurrentDictionary<string, PendingMarkPrice> latestPrices,
        string symbol,
        long generation)
    {
        if (latestPrices.TryGetValue(symbol, out var current)
            && current.Generation == generation)
        {
            ((ICollection<KeyValuePair<string, PendingMarkPrice>>)latestPrices)
                .Remove(new KeyValuePair<string, PendingMarkPrice>(symbol, current));
        }
    }

    internal enum SubscriptionLifecycle
    {
        Starting,
        Active,
        Stopping,
        Failed,
    }

    internal sealed class SubscriptionState(string symbol, long generation)
    {
        public string Symbol { get; } = symbol;
        public long Generation { get; } = generation;
        public int IsActive = 1;
        public int FailureRequested;
        public int TimedOut;
        public int AttemptCompletionObserved;
        public SubscriptionLifecycle Lifecycle { get; set; } = SubscriptionLifecycle.Starting;
        public CancellationTokenSource? Cancellation { get; set; }
        public CancellationTokenSource? AttemptTimeout { get; set; }
        public CancellationTokenRegistration TimeoutRegistration { get; set; }
        public Task<IAsyncDisposable>? SubscribeTask { get; set; }
        public IAsyncDisposable? Lease { get; set; }
        public Task? DisposeTask { get; set; }
        public bool StartUnconfirmed { get; set; }
        public bool DisposeUnconfirmed { get; set; }
        public long StartTimestamp { get; set; }
        public DateTimeOffset? LastPersistedObservedAt { get; set; }
        public long? LastPersistAttemptTimestamp { get; set; }
    }

    internal sealed record PendingMarkPrice(long Generation, MarketMarkPrice Price);

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
        EventId = 1227,
        Level = LogLevel.Error,
        Message = "Первоначальное создание публичной рыночной подписки завершилось неопределённо; повторная подписка инструмента заблокирована до завершения процесса. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionStartUnconfirmed(
        ILogger logger,
        string exceptionType);

    [LoggerMessage(
        EventId = 1223,
        Level = LogLevel.Error,
        Message = "Закрытие публичной рыночной подписки не подтверждено; повторное закрытие и новая подписка инструмента заблокированы до завершения процесса. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionDisposeFailed(
        ILogger logger,
        string exceptionType);

    [LoggerMessage(
        EventId = 1226,
        Level = LogLevel.Error,
        Message = "Закрытие публичной рыночной подписки не завершилось за ограниченное время остановки; lease сохранён до завершения процесса.")]
    private static partial void LogSubscriptionDisposePendingAtShutdown(ILogger logger);

    [LoggerMessage(
        EventId = 1224,
        Level = LogLevel.Warning,
        Message = "Не удалось отменить создание публичной рыночной подписки. ExceptionType={ExceptionType}")]
    private static partial void LogSubscriptionCancellationFailed(
        ILogger logger,
        string exceptionType);
}
