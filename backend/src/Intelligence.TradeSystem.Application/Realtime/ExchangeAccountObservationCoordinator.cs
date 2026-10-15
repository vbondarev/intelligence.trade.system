using System.Collections.Concurrent;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Единая точка последовательного применения REST-синхронизаций и приватных потоковых наблюдений
/// одного биржевого аккаунта: периодической, ручной, стартовой и recovery-синхронизации.
/// </summary>
/// <remarks>
/// <para>
/// Операции одного аккаунта выполняются последовательно внутри процесса; это не заменяет PostgreSQL
/// concurrency control конкурентных транзакций. Потоковые наблюдения принимаются без ожидания в
/// ограниченный буфер аккаунта и применяются <see cref="ProcessPendingAsync"/> после завершения
/// текущей REST-синхронизации.
/// </para>
/// <para>
/// Наблюдение позиции, время которого попадает в окно REST-запроса, могло быть уже отражено этим
/// снимком; его новизна не доказана, поэтому состояние слота подтверждается scoped REST-наблюдением.
/// Наблюдение кошелька из такого окна отбрасывается: более новое значение придёт следующим сообщением.
/// </para>
/// <para>
/// Переполнение буфера, необработанное исключение применения и неоднозначный порядок означают
/// возможный пропуск событий: состояние наблюдения помечается как требующее recovery и выполняется
/// полная REST-сверка. Признак recovery снимается только сверкой, во время которой события не терялись.
/// </para>
/// </remarks>
public sealed class ExchangeAccountObservationCoordinator(
    IServiceScopeFactory scopeFactory,
    TimeProvider? timeProvider = null,
    int bufferCapacity = ExchangeAccountObservationCoordinator.DefaultBufferCapacity)
    : IExchangeAccountSyncService
{
    public const int DefaultBufferCapacity = 256;
    private const int MaxStateAttempts = 3;

    private readonly ConcurrentDictionary<(UserId UserId, ExchangeAccountId AccountId), AccountChannel> channels = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly int capacity = bufferCapacity > 0
        ? bufferCapacity
        : throw new ArgumentOutOfRangeException(nameof(bufferCapacity), bufferCapacity, "Buffer capacity must be positive.");

    public async Task<ExchangeAccountSyncResult> SynchronizeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var channel = GetChannel(userId, exchangeAccountId);
        await channel.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SynchronizeUnderGateAsync(userId, exchangeAccountId, channel, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            channel.Gate.Release();
        }
    }

    /// <summary>
    /// Помещает наблюдение позиции в буфер аккаунта без ожидания.
    /// </summary>
    /// <returns>
    /// <see langword="false"/>, если поколение устарело или буфер переполнен; переполнение
    /// обрабатывается следующим вызовом <see cref="ProcessPendingAsync"/> как пропуск событий.
    /// </returns>
    public bool TryEnqueue(UserId userId, long generation, PrivatePositionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return TryEnqueue(userId, observation.ExchangeAccountId, new PendingObservation(generation, observation, null));
    }

    /// <inheritdoc cref="TryEnqueue(UserId, long, PrivatePositionObservation)" />
    public bool TryEnqueue(UserId userId, long generation, PrivateWalletObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return TryEnqueue(userId, observation.ExchangeAccountId, new PendingObservation(generation, null, observation));
    }

    /// <summary>
    /// Последовательно применяет буферизованные наблюдения аккаунта и выполняет требуемую ими
    /// REST-сверку.
    /// </summary>
    /// <returns>Итоги применения в порядке обработки.</returns>
    public async Task<IReadOnlyList<RealtimeObservationApplyResult>> ProcessPendingAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var channel = GetChannel(userId, exchangeAccountId);
        var results = new List<RealtimeObservationApplyResult>();
        await channel.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recoveryReported = false;
            while (true)
            {
                PendingObservation? next;
                lock (channel.Sync)
                {
                    if (channel.HasUnhandledRecoveryRequest)
                    {
                        // Сверка охватывает все события, сформированные до её начала, поэтому
                        // ожидающие наблюдения не применяются поверх её результата.
                        channel.Pending.Clear();
                        next = null;
                    }
                    else if (!channel.Pending.TryDequeue(out next))
                    {
                        break;
                    }
                }

                if (next is null)
                {
                    if (!recoveryReported)
                    {
                        results.Add(RealtimeObservationApplyResult.RecoveryRequired(
                            RealtimeObservationApplyResult.BufferOverflow));
                    }

                    recoveryReported = false;
                    await SynchronizeUnderGateAsync(userId, exchangeAccountId, channel, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                RealtimeObservationApplyResult result;
                try
                {
                    result = await ApplyAsync(userId, channel, next, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    lock (channel.Sync)
                    {
                        channel.RecoveryRequests++;
                    }

                    throw;
                }

                results.Add(result);
                switch (result.Outcome)
                {
                    case RealtimeObservationApplyOutcome.RecoveryRequired:
                        lock (channel.Sync)
                        {
                            channel.RecoveryRequests++;
                        }

                        recoveryReported = true;
                        break;
                    case RealtimeObservationApplyOutcome.GenerationSuperseded:
                    case RealtimeObservationApplyOutcome.AccountUnavailable:
                        lock (channel.Sync)
                        {
                            if (channel.Generation == next.Generation)
                            {
                                channel.Pending.Clear();
                            }
                        }

                        break;
                }
            }
        }
        finally
        {
            channel.Gate.Release();
        }

        return results;
    }

    private bool TryEnqueue(UserId userId, ExchangeAccountId exchangeAccountId, PendingObservation pending)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pending.Generation, 0, "generation");
        var channel = GetChannel(userId, exchangeAccountId);
        lock (channel.Sync)
        {
            if (pending.Generation < channel.Generation)
            {
                return false;
            }

            if (pending.Generation > channel.Generation)
            {
                // Новое поколение начинается с собственной стартовой сверки; наблюдения прежнего
                // поколения к нему не относятся.
                channel.Generation = pending.Generation;
                channel.Pending.Clear();
                channel.HandledRecoveryRequests = channel.RecoveryRequests;
            }

            if (channel.Pending.Count >= capacity)
            {
                channel.RecoveryRequests++;
                return false;
            }

            channel.Pending.Enqueue(pending);
            return true;
        }
    }

    private async Task<RealtimeObservationApplyResult> ApplyAsync(
        UserId userId,
        AccountChannel channel,
        PendingObservation pending,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        if (pending.Position is { } position)
        {
            var observedAt = RealtimePositionReconciliationService.ResolveObservedAt(position);
            bool overlapsRest;
            lock (channel.Sync)
            {
                overlapsRest = observedAt is { } at &&
                    (channel.FullSyncWindow?.Contains(at) == true ||
                     channel.SymbolWindows.TryGetValue(position.Symbol, out var window) && window.Contains(at));
            }

            var startedAt = clock.GetUtcNow();
            var result = await scope.ServiceProvider
                .GetRequiredService<RealtimePositionReconciliationService>()
                .ApplyAsync(userId, pending.Generation, position, overlapsRest, cancellationToken)
                .ConfigureAwait(false);
            if (result.ConfirmedByRest)
            {
                var window = new RestWindow(startedAt, clock.GetUtcNow());
                lock (channel.Sync)
                {
                    channel.SymbolWindows[position.Symbol] = window;
                }
            }

            return result;
        }

        var wallet = pending.Wallet!;
        var walletObservedAt = RealtimeWalletObservationService.ResolveObservedAt(wallet);
        lock (channel.Sync)
        {
            if (walletObservedAt is { } at && channel.FullSyncWindow is { } fullSync && at <= fullSync.CompletedAt)
            {
                return RealtimeObservationApplyResult.Stale(RealtimeObservationApplyResult.OverlapsRestObservation);
            }
        }

        return await scope.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(userId, pending.Generation, wallet, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ExchangeAccountSyncResult> SynchronizeUnderGateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        AccountChannel channel,
        CancellationToken cancellationToken)
    {
        long generation;
        long recoveryRequestsAtStart;
        bool recoveryRequested;
        lock (channel.Sync)
        {
            generation = channel.Generation;
            recoveryRequestsAtStart = channel.RecoveryRequests;
            recoveryRequested = channel.HasUnhandledRecoveryRequest;
            channel.HandledRecoveryRequests = recoveryRequestsAtStart;
        }

        if (recoveryRequested && generation > 0)
        {
            await SetRecoveryRequiredAsync(userId, exchangeAccountId, generation, cancellationToken)
                .ConfigureAwait(false);
        }

        var startedAt = clock.GetUtcNow();
        ExchangeAccountSyncResult result;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            result = await scope.ServiceProvider
                .GetRequiredService<ExchangeAccountSyncService>()
                .SynchronizeAsync(userId, exchangeAccountId, cancellationToken)
                .ConfigureAwait(false);
        }

        var completedAt = clock.GetUtcNow();
        bool eventsMayBeMissing;
        lock (channel.Sync)
        {
            channel.FullSyncWindow = new RestWindow(startedAt, completedAt);
            eventsMayBeMissing = channel.RecoveryRequests != recoveryRequestsAtStart || channel.Generation != generation;
        }

        if (generation > 0 &&
            !eventsMayBeMissing &&
            result is { Outcome: ExchangeAccountSyncOutcome.Synchronized, PortfolioState.PositionsFullyReconciled: true })
        {
            await SetReconciledAsync(userId, exchangeAccountId, generation, completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task SetRecoveryRequiredAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        long generation,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRealtimeObservationStateStore>();
        for (var attempt = 1; ; attempt++)
        {
            var current = await store.GetAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Value.Generation != generation || current.Value.RecoveryRequired)
            {
                return;
            }

            try
            {
                await store
                    .SaveAsync(userId, current.Value with { RecoveryRequired = true }, current.Version, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxStateAttempts)
            {
            }
        }
    }

    /// <summary>
    /// Снимает признак recovery после полной сверки. Проигранная гонка за состояние оставляет признак
    /// установленным: это консервативно, его снимет следующая успешная сверка.
    /// </summary>
    private async Task SetReconciledAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        long generation,
        DateTimeOffset reconciledAt,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRealtimeObservationStateStore>();
        for (var attempt = 1; attempt <= MaxStateAttempts; attempt++)
        {
            var current = await store.GetAsync(userId, exchangeAccountId, cancellationToken).ConfigureAwait(false);
            if (current is null || current.Value.Generation != generation)
            {
                return;
            }

            try
            {
                await store
                    .SaveAsync(
                        userId,
                        current.Value with { RecoveryRequired = false, LastFullyReconciledAt = reconciledAt },
                        current.Version,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (ConcurrencyConflictException)
            {
            }
        }
    }

    private AccountChannel GetChannel(UserId userId, ExchangeAccountId exchangeAccountId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        return channels.GetOrAdd((userId, exchangeAccountId), static _ => new AccountChannel());
    }

    private sealed record PendingObservation(
        long Generation,
        PrivatePositionObservation? Position,
        PrivateWalletObservation? Wallet);

    private readonly record struct RestWindow(DateTimeOffset StartedAt, DateTimeOffset CompletedAt)
    {
        public bool Contains(DateTimeOffset observedAt) => observedAt >= StartedAt && observedAt <= CompletedAt;
    }

    private sealed class AccountChannel
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public object Sync { get; } = new();

        public Queue<PendingObservation> Pending { get; } = new();

        public Dictionary<string, RestWindow> SymbolWindows { get; } = new(StringComparer.OrdinalIgnoreCase);

        public long Generation { get; set; }

        public long RecoveryRequests { get; set; }

        public long HandledRecoveryRequests { get; set; }

        public RestWindow? FullSyncWindow { get; set; }

        public bool HasUnhandledRecoveryRequest => RecoveryRequests != HandledRecoveryRequests;
    }
}
