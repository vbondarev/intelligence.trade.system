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
/// Наблюдение кошелька окном не фильтруется: его новизну относительно фактически принятого баланса
/// определяет <see cref="RealtimeWalletObservationService"/> в транзакции под блокировкой аккаунта.
/// </para>
/// <para>
/// Переполнение буфера, необработанное исключение применения и неоднозначный порядок означают
/// возможный пропуск событий: состояние наблюдения помечается как требующее recovery и выполняется
/// полная REST-сверка. Признак recovery снимается только полной сверкой текущего запроса, во время
/// которой события не терялись. Потеря во время любой сверки или записи её итога снова устанавливает
/// persisted-признак текущего поколения.
/// </para>
/// <para>
/// Канал аккаунта существует, пока его удерживает операция, и после неё — только если хранит
/// ожидающие наблюдения, необработанный recovery или окна REST-запросов аккаунта, наблюдаемого
/// потоком. Каналы ненайденных, чужих и отключённых аккаунтов не накапливаются.
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

    private readonly Dictionary<(UserId UserId, ExchangeAccountId AccountId), AccountChannel> channels = new();
    private readonly object channelsSync = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly int capacity = bufferCapacity > 0
        ? bufferCapacity
        : throw new ArgumentOutOfRangeException(nameof(bufferCapacity), bufferCapacity, "Buffer capacity must be positive.");

    /// <summary>Количество каналов аккаунтов, удерживаемых координатором.</summary>
    internal int ChannelCount
    {
        get
        {
            lock (channelsSync)
            {
                return channels.Count;
            }
        }
    }

    public async Task<ExchangeAccountSyncResult> SynchronizeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var key = CreateKey(userId, exchangeAccountId);
        var channel = AcquireChannel(key);
        try
        {
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
        finally
        {
            ReleaseChannel(key, channel);
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
    /// <returns>
    /// Итоги ограниченного прохода в порядке обработки. Один проход применяет не больше ёмкости
    /// буфера и выполняет не больше одной recovery-сверки; вызывающий код обязан повторить вызов,
    /// если новые наблюдения продолжают поступать.
    /// </returns>
    public async Task<IReadOnlyList<RealtimeObservationApplyResult>> ProcessPendingAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var key = CreateKey(userId, exchangeAccountId);
        var channel = AcquireChannel(key);
        try
        {
            await channel.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProcessPendingUnderGateAsync(userId, exchangeAccountId, channel, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                channel.Gate.Release();
            }
        }
        finally
        {
            ReleaseChannel(key, channel);
        }
    }

    private async Task<IReadOnlyList<RealtimeObservationApplyResult>> ProcessPendingUnderGateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        AccountChannel channel,
        CancellationToken cancellationToken)
    {
        var results = new List<RealtimeObservationApplyResult>();
        var recoveryReported = false;
        var recoveryAttempted = false;
        var processedObservations = 0;
        while (processedObservations < capacity)
        {
            PendingObservation? next;
            var recoveryRequested = false;
            lock (channel.Sync)
            {
                if (channel.HasUnhandledRecoveryRequest)
                {
                    // Сверка охватывает все события, сформированные до её начала, поэтому
                    // ожидающие наблюдения не применяются поверх её результата.
                    channel.Pending.Clear();
                    recoveryRequested = true;
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

                recoveryReported = true;
                if (recoveryAttempted)
                {
                    break;
                }

                recoveryAttempted = true;
                await SynchronizeUnderGateAsync(userId, exchangeAccountId, channel, cancellationToken)
                    .ConfigureAwait(false);

                lock (channel.Sync)
                {
                    recoveryRequested = channel.HasUnhandledRecoveryRequest;
                }

                // До успешного подтверждения recovery частичные observations не могут
                // безопасно дополнять состояние, потерявшее полноту.
                if (recoveryRequested)
                {
                    break;
                }

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

            processedObservations++;
            results.Add(result);
            switch (result.Outcome)
            {
                case RealtimeObservationApplyOutcome.Applied:
                    lock (channel.Sync)
                    {
                        channel.AccountUnavailable = false;
                    }

                    break;
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

                        if (result.Outcome == RealtimeObservationApplyOutcome.AccountUnavailable)
                        {
                            channel.AccountUnavailable = true;
                        }
                    }

                    break;
            }
        }

        return results;
    }

    private bool TryEnqueue(UserId userId, ExchangeAccountId exchangeAccountId, PendingObservation pending)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pending.Generation, 0, "generation");
        var key = CreateKey(userId, exchangeAccountId);
        var channel = AcquireChannel(key);
        try
        {
            return TryEnqueue(channel, pending);
        }
        finally
        {
            ReleaseChannel(key, channel);
        }
    }

    private bool TryEnqueue(AccountChannel channel, PendingObservation pending)
    {
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
                var recoveryWasRequired = channel.HasUnhandledRecoveryRequest;
                channel.Generation = pending.Generation;
                channel.Pending.Clear();
                channel.HandledRecoveryRequests = channel.RecoveryRequests;
                if (recoveryWasRequired)
                {
                    // Успешная сверка старого поколения не доказывает полноту нового.
                    channel.RecoveryRequests++;
                }
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

        return await scope.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(userId, pending.Generation, pending.Wallet!, cancellationToken)
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
            channel.AccountUnavailable = result.Outcome is
                ExchangeAccountSyncOutcome.NotFound or ExchangeAccountSyncOutcome.AccountDisabled;
            eventsMayBeMissing = channel.RecoveryRequests != recoveryRequestsAtStart || channel.Generation != generation;
        }

        var reconciled = false;
        if (generation > 0 && !eventsMayBeMissing && result.FullReconciliationCommitted)
        {
            reconciled = await SetReconciledAsync(userId, exchangeAccountId, generation, completedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        // Счётчик перепроверяется после записи Reconciled независимо от запроса на входе: потеря во время
        // REST или самой записи могла не попасть в persisted-признак. Для нового поколения признак
        // устанавливается его собственным стартом.
        var newRecoveryRequested = false;
        lock (channel.Sync)
        {
            if (channel.Generation == generation)
            {
                if (channel.RecoveryRequests == recoveryRequestsAtStart)
                {
                    if (reconciled)
                    {
                        channel.HandledRecoveryRequests = recoveryRequestsAtStart;
                    }
                }
                else
                {
                    newRecoveryRequested = channel.HasUnhandledRecoveryRequest;
                }
            }
        }

        if (newRecoveryRequested && generation > 0)
        {
            // Локальный запрос остаётся необработанным; при ошибке записи его сохранит следующий проход.
            await SetRecoveryRequiredAsync(userId, exchangeAccountId, generation, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<bool> SetRecoveryRequiredAsync(
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
                return current is { Value.Generation: var currentGeneration } &&
                    currentGeneration == generation &&
                    current.Value.RecoveryRequired;
            }

            try
            {
                await store
                    .SaveAsync(userId, current.Value with { RecoveryRequired = true }, current.Version, cancellationToken)
                    .ConfigureAwait(false);
                return true;
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
    private async Task<bool> SetReconciledAsync(
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
                return false;
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
                return true;
            }
            catch (ConcurrencyConflictException)
            {
            }
        }

        return false;
    }

    private static (UserId UserId, ExchangeAccountId AccountId) CreateKey(
        UserId userId,
        ExchangeAccountId exchangeAccountId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }

        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        return (userId, exchangeAccountId);
    }

    /// <summary>
    /// Возвращает канал аккаунта, удерживая его от удаления до <see cref="ReleaseChannel"/>.
    /// </summary>
    /// <remarks>
    /// Канал удаляется только без удерживающих операций под той же блокировкой, под которой выдаётся,
    /// поэтому для одного аккаунта не существует двух каналов с разными gate.
    /// </remarks>
    private AccountChannel AcquireChannel((UserId UserId, ExchangeAccountId AccountId) key)
    {
        lock (channelsSync)
        {
            if (!channels.TryGetValue(key, out var channel))
            {
                channel = new AccountChannel();
                channels.Add(key, channel);
            }

            channel.Leases++;
            return channel;
        }
    }

    private void ReleaseChannel((UserId UserId, ExchangeAccountId AccountId) key, AccountChannel channel)
    {
        lock (channelsSync)
        {
            channel.Leases--;
            if (channel.Leases > 0)
            {
                return;
            }

            lock (channel.Sync)
            {
                if (!channel.IsReleasable)
                {
                    return;
                }
            }

            channels.Remove(key);
        }
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

        /// <summary>Число операций, удерживающих канал; изменяется только под блокировкой словаря каналов.</summary>
        public int Leases { get; set; }

        /// <summary>Последняя операция установила, что аккаунт пользователя не найден или отключён.</summary>
        public bool AccountUnavailable { get; set; }

        public bool HasUnhandledRecoveryRequest => RecoveryRequests != HandledRecoveryRequests;

        /// <summary>
        /// Канал не хранит состояния, которое нельзя потерять: ожидающих наблюдений и необработанного
        /// recovery нет, а окна REST-запросов не нужны, пока аккаунт не наблюдается потоком или недоступен.
        /// </summary>
        public bool IsReleasable =>
            Pending.Count == 0 &&
            !HasUnhandledRecoveryRequest &&
            (Generation == 0 || AccountUnavailable);
    }
}
