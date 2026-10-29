using System.Collections.Concurrent;
using System.Threading.Channels;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

public sealed class ExchangeRealtimeObservationSupervisor(
    IServiceScopeFactory scopeFactory,
    IExchangeRealtimeProviderFactory providerFactory,
    ExchangeAccountObservationCoordinator coordinator,
    IOptions<ExchangeRealtimeObservationOptions> options,
    TimeProvider timeProvider,
    ILogger<ExchangeRealtimeObservationSupervisor> logger)
{
    private const int CandidateBatchSize = 100;
    private const int MaxStateWriteAttempts = 3;

    private readonly ExchangeRealtimeObservationOptions settings = options.Value;
    private readonly ConcurrentDictionary<AccountKey, ActiveAccount> activeAccounts = new();
    private readonly Dictionary<AccountKey, RetryState> retries = [];
    private readonly Channel<ActiveAccount> workQueue = Channel.CreateBounded<ActiveAccount>(
        new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!settings.Enabled)
        {
            ExchangeRealtimeObservationLogMessages.LogDisabled(logger);
            return;
        }

        var consumers = Enumerable.Range(0, settings.AccountConsumerCount)
            .Select(_ => ConsumeAsync(cancellationToken))
            .ToArray();
        var loops = new Task[]
        {
            DiscoverAccountsAsync(cancellationToken),
            WriteHeartbeatsAsync(cancellationToken),
        };

        try
        {
            await Task.WhenAll(consumers.Concat(loops)).ConfigureAwait(false);
        }
        finally
        {
            ExchangeRealtimeObservationLogMessages.LogStopping(logger);
            workQueue.Writer.TryComplete();
            await StopAllAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task DiscoverAccountsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DiscoverOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                ExchangeRealtimeObservationLogMessages.LogDiscoveryFailed(
                    logger,
                    exception.GetType().FullName);
            }

            ScheduleAllActive();

            try
            {
                await Task.Delay(
                        settings.AccountDiscoveryInterval,
                        timeProvider,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task DiscoverOnceAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        using var activity = ExchangeRealtimeObservationTelemetry.StartDiscoveryActivity();
        ExchangeRealtimeObservationLogMessages.LogDiscoveryStarted(logger);

        var candidates = await LoadCandidatesAsync(cancellationToken).ConfigureAwait(false);
        if (candidates is null)
        {
            return;
        }

        var discovered = new HashSet<AccountKey>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new AccountKey(candidate.UserId, candidate.ExchangeAccountId);
            if (!discovered.Add(key))
            {
                continue;
            }

            try
            {
                await ReconcileCandidateAsync(candidate, key, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ExchangeRealtimeObservationLogMessages.LogAccountProcessingFailed(
                    logger,
                    candidate.UserId.Value,
                    candidate.ExchangeAccountId.Value,
                    exception.GetType().FullName);
            }
        }

        foreach (var pair in activeAccounts.ToArray())
        {
            if (!discovered.Contains(pair.Key))
            {
                await StopSessionAsync(pair.Value, advanceGeneration: true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        ExchangeRealtimeObservationTelemetry.RecordDiscovery(
            timeProvider.GetElapsedTime(startedAt));
    }

    private async Task<IReadOnlyList<ExchangeAccountSyncCandidate>?> LoadCandidatesAsync(
        CancellationToken cancellationToken)
    {
        var candidates = new List<ExchangeAccountSyncCandidate>();
        var seen = new HashSet<ExchangeAccountId>();
        ExchangeAccountId? after = null;

        while (true)
        {
            IReadOnlyList<ExchangeAccountSyncCandidate> batch;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                batch = await scope.ServiceProvider
                    .GetRequiredService<IExchangeAccountSyncCandidateSource>()
                    .GetBatchAsync(after, CandidateBatchSize, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ExchangeRealtimeObservationLogMessages.LogDiscoveryFailed(
                    logger,
                    exception.GetType().FullName);
                return null;
            }

            if (batch.Count == 0)
            {
                return candidates;
            }

            candidates.AddRange(batch.Where(candidate => seen.Add(candidate.ExchangeAccountId)));
            var nextAfter = batch[^1].ExchangeAccountId;
            if (after == nextAfter)
            {
                ExchangeRealtimeObservationLogMessages.LogCursorDidNotAdvance(logger);
                return null;
            }

            after = nextAfter;
            if (batch.Count < CandidateBatchSize)
            {
                return candidates;
            }
        }
    }

    private async Task ReconcileCandidateAsync(
        ExchangeAccountSyncCandidate candidate,
        AccountKey key,
        CancellationToken cancellationToken)
    {
        var descriptor = await ReadDescriptorAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        if (descriptor is null || descriptor.CredentialVersion is null)
        {
            retries.Remove(key);
            if (activeAccounts.TryGetValue(key, out var existing))
            {
                ExchangeRealtimeObservationLogMessages.LogAccountNoLongerConnected(
                    logger,
                    key.UserId.Value,
                    key.ExchangeAccountId.Value);
                await StopSessionAsync(existing, advanceGeneration: true, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        var credentialVersion = descriptor.CredentialVersion.Value;
        if (activeAccounts.TryGetValue(key, out var active)
            && !active.IsStopping
            && active.CredentialVersion == credentialVersion)
        {
            var currentState = await ReadObservationStateAsync(key, cancellationToken)
                .ConfigureAwait(false);
            if (currentState is not null
                && currentState.Value.Generation == active.Generation
                && (currentState.Value.TransportStatus != RealtimeObservationTransportStatus.Connected
                    || currentState.Value.IsTransportOperational(
                        timeProvider.GetUtcNow(),
                        settings.HeartbeatStaleAfter)))
            {
                retries.Remove(key);
                return;
            }
        }

        if (retries.TryGetValue(key, out var retry)
            && timeProvider.GetUtcNow() < retry.NextAttemptAt)
        {
            return;
        }

        await StartSessionAsync(
                candidate,
                descriptor.Account,
                credentialVersion,
                activeAccounts.GetValueOrDefault(key),
                key,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AccountDescriptor?> ReadDescriptorAsync(
        ExchangeAccountSyncCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var account = await scope.ServiceProvider
            .GetRequiredService<IExchangeAccountRepository>()
            .GetByIdAsync(candidate.UserId, candidate.ExchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null
            || account.Value.ExchangeId != ExchangeId.Bybit
            || account.Value.ConnectionStatus != ExchangeAccountConnectionStatus.Connected)
        {
            return null;
        }

        var credential = await scope.ServiceProvider
            .GetRequiredService<IExchangeAccountCredentialStore>()
            .GetMetadataAsync(candidate.UserId, candidate.ExchangeAccountId, cancellationToken)
            .ConfigureAwait(false);

        return new AccountDescriptor(account.Value, credential?.Version);
    }

    private async Task StartSessionAsync(
        ExchangeAccountSyncCandidate candidate,
        ExchangeAccount account,
        ConcurrencyVersion credentialVersion,
        ActiveAccount? previous,
        AccountKey key,
        CancellationToken cancellationToken)
    {
        var state = previous is { IsGenerationFenced: true }
            ? await ReadObservationStateAsync(key, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Состояние realtime-наблюдения исчезло после установки generation fence предыдущего провайдера.")
            : await AdvanceGenerationAsync(
                    key,
                    RealtimeObservationTransportStatus.Initializing,
                    cancellationToken)
                .ConfigureAwait(false);

        if (previous is { IsGenerationFenced: true }
            && state.Value.TransportStatus != RealtimeObservationTransportStatus.Initializing)
        {
            state = await AdvanceGenerationAsync(
                    key,
                    RealtimeObservationTransportStatus.Initializing,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (previous is not null)
        {
            previous.MarkGenerationFenced(state.Value.Generation);
            previous.BeginStopping();
            if (!await DisposeProviderAsync(previous).ConfigureAwait(false))
            {
                return;
            }

            activeAccounts.TryRemove(key, out _);
        }

        IExchangeRealtimeProvider? provider = null;
        ActiveAccount? session = null;
        try
        {
            provider = await CreateProviderAsync(
                    key,
                    account,
                    credentialVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            if (provider is null)
            {
                await UpdateDegradedStateAsync(
                        key,
                        state.Value.Generation,
                        "CredentialVersionChanged",
                        cancellationToken)
                    .ConfigureAwait(false);
                ScheduleRetry(key);
                return;
            }

            session = new ActiveAccount(
                this,
                coordinator,
                key,
                state.Value.Generation,
                credentialVersion,
                provider);
            activeAccounts[key] = session;
            var transportState = await provider.StartAsync(session, cancellationToken)
                .ConfigureAwait(false);

            if (transportState.Status != RealtimeTransportStatus.Subscribed)
            {
                await PersistTransportStateAsync(
                        session,
                        transportState,
                        recoveryRequestCount: 0,
                        cancellationToken)
                    .ConfigureAwait(false);
                session.BeginStopping();
                if (await DisposeProviderAsync(session).ConfigureAwait(false))
                {
                    activeAccounts.TryRemove(key, out _);
                }

                ScheduleRetry(key);
                return;
            }

            await PersistTransportStateAsync(
                    session,
                    transportState,
                    recoveryRequestCount: 0,
                    cancellationToken)
                .ConfigureAwait(false);
            ExchangeRealtimeObservationTelemetry.RecordProviderStarted();
            ExchangeRealtimeObservationLogMessages.LogProviderStarted(
                logger,
                key.UserId.Value,
                key.ExchangeAccountId.Value,
                session.Generation);

            try
            {
                var result = await coordinator
                    .SynchronizeAsync(key.UserId, key.ExchangeAccountId, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.FullReconciliationCommitted)
                {
                    ExchangeRealtimeObservationLogMessages.LogInitialReconciliationIncomplete(
                        logger,
                        key.UserId.Value,
                        key.ExchangeAccountId.Value,
                        result.Outcome);
                }

                await coordinator
                    .ProcessPendingAsync(key.UserId, key.ExchangeAccountId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ExchangeRealtimeObservationLogMessages.LogAccountProcessingFailed(
                    logger,
                    key.UserId.Value,
                    key.ExchangeAccountId.Value,
                    exception.GetType().FullName);
            }

            session.MarkReady();
            retries.Remove(key);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (session is not null)
            {
                session.BeginStopping();
                if (await DisposeProviderAsync(session).ConfigureAwait(false))
                {
                    activeAccounts.TryRemove(key, out _);
                }
            }
            else if (provider is not null)
            {
                await DisposeProviderAsync(provider, key).ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception)
        {
            ExchangeRealtimeObservationLogMessages.LogProviderStartFailed(
                logger,
                key.UserId.Value,
                key.ExchangeAccountId.Value,
                exception.GetType().FullName);
            if (session is not null)
            {
                session.BeginStopping();
                if (await DisposeProviderAsync(session).ConfigureAwait(false))
                {
                    activeAccounts.TryRemove(key, out _);
                }
            }
            else if (provider is not null)
            {
                await DisposeProviderAsync(provider, key).ConfigureAwait(false);
            }

            await UpdateDegradedStateAsync(
                    key,
                    state.Value.Generation,
                    "ProviderStartFailed",
                    CancellationToken.None)
                .ConfigureAwait(false);
            ScheduleRetry(key);
        }
    }

    private async Task<IExchangeRealtimeProvider?> CreateProviderAsync(
        AccountKey key,
        ExchangeAccount account,
        ConcurrencyVersion expectedCredentialVersion,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var credentials = await scope.ServiceProvider
            .GetRequiredService<IExchangeAccountCredentialStore>()
            .GetAsync(key.UserId, key.ExchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (credentials is null || credentials.Version != expectedCredentialVersion)
        {
            return null;
        }

        return providerFactory.Create(account.ExchangeId, account.Id, credentials);
    }

    private async Task ProcessSessionAsync(
        ActiveAccount session,
        CancellationToken cancellationToken)
    {
        var recoverySyncAttempted = false;
        var (transportState, recoveryRequestCount) = session.TakeTransportState();
        if (transportState is not null)
        {
            var saved = await PersistTransportStateAsync(
                    session,
                    transportState,
                    recoveryRequestCount,
                    cancellationToken)
                .ConfigureAwait(false);
            if (saved && (transportState.RecoveryRequired || recoveryRequestCount > 0))
            {
                ExchangeRealtimeObservationTelemetry.RecordRecoveryRequested(
                    Math.Max(1, recoveryRequestCount));
                await coordinator
                    .SynchronizeAsync(session.Key.UserId, session.Key.ExchangeAccountId, cancellationToken)
                    .ConfigureAwait(false);
                recoverySyncAttempted = true;
            }
        }

        if (!recoverySyncAttempted)
        {
            var currentState = await ReadObservationStateAsync(session.Key, cancellationToken)
                .ConfigureAwait(false);
            if (currentState is not null
                && currentState.Value.Generation == session.Generation
                && currentState.Value.RecoveryRequired)
            {
                ExchangeRealtimeObservationTelemetry.RecordRecoveryRequested(1);
                await coordinator
                    .SynchronizeAsync(session.Key.UserId, session.Key.ExchangeAccountId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var results = await coordinator
            .ProcessPendingAsync(session.Key.UserId, session.Key.ExchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (results.Count >= settings.QueueCapacity)
        {
            session.RequestWork();
        }
    }

    private async Task<bool> PersistTransportStateAsync(
        ActiveAccount session,
        RealtimeTransportState transportState,
        long recoveryRequestCount,
        CancellationToken cancellationToken)
    {
        var connected = transportState.Status is RealtimeTransportStatus.Subscribed
            or RealtimeTransportStatus.Restored;
        session.SetTransportConnected(connected);
        var now = timeProvider.GetUtcNow();
        var saved = await UpdateCurrentStateAsync(
                session.Key,
                session.Generation,
                current => current with
                {
                    TransportStatus = MapTransportStatus(transportState.Status),
                    LastConnectedAt = connected ? transportState.OccurredAt : current.LastConnectedAt,
                    LastHeartbeatAt = connected ? now : null,
                    RecoveryRequired = current.RecoveryRequired
                        || transportState.RecoveryRequired
                        || recoveryRequestCount > 0,
                    RecoveryRequestEpoch = checked(
                        current.RecoveryRequestEpoch + recoveryRequestCount),
                    FailureCode = transportState.Status == RealtimeTransportStatus.Subscribed
                        ? null
                        : transportState.Failure?.Kind.ToString() ?? transportState.Status.ToString(),
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (saved && (transportState.RecoveryRequired || recoveryRequestCount > 0))
        {
            ExchangeRealtimeObservationLogMessages.LogRecoveryRequired(
                logger,
                session.Key.UserId.Value,
                session.Key.ExchangeAccountId.Value,
                transportState.Status);
        }

        return saved;
    }

    private async Task WriteHeartbeatsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(settings.HeartbeatInterval, timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var sessions = activeAccounts.Values
                    .Where(session => session.IsReady
                        && session.IsTransportConnected
                        && !session.IsStopping)
                    .ToArray();
                await Parallel.ForEachAsync(
                        sessions,
                        new ParallelOptions
                        {
                            CancellationToken = cancellationToken,
                            MaxDegreeOfParallelism = settings.AccountConsumerCount,
                        },
                        async (session, token) =>
                        {
                            if (session.IsStopping)
                            {
                                return;
                            }

                            try
                            {
                                var now = timeProvider.GetUtcNow();
                                var saved = await UpdateCurrentStateAsync(
                                        session.Key,
                                        session.Generation,
                                        current => current.TransportStatus
                                            == RealtimeObservationTransportStatus.Connected
                                                ? current with { LastHeartbeatAt = now }
                                                : current,
                                        token)
                                    .ConfigureAwait(false);
                                if (saved)
                                {
                                    ExchangeRealtimeObservationTelemetry.RecordHeartbeat();
                                }
                            }
                            catch (OperationCanceledException) when (token.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception exception)
                            {
                                ExchangeRealtimeObservationLogMessages.LogStateUpdateFailed(
                                    logger,
                                    session.Key.UserId.Value,
                                    session.Key.ExchangeAccountId.Value,
                                    exception.GetType().FullName);
                            }
                        })
                    .ConfigureAwait(false);

                ScheduleAllActive();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await workQueue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (workQueue.Reader.TryRead(out var session))
                {
                    var observedWorkVersion = session.MarkDequeued();
                    if (!IsCurrentSession(session))
                    {
                        session.CancelSchedule();
                        continue;
                    }

                    try
                    {
                        await ProcessSessionAsync(session, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        ExchangeRealtimeObservationLogMessages.LogAccountProcessingFailed(
                            logger,
                            session.Key.UserId.Value,
                            session.Key.ExchangeAccountId.Value,
                            exception.GetType().FullName);
                    }
                    finally
                    {
                        session.MarkProcessed(observedWorkVersion);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> UpdateCurrentStateAsync(
        AccountKey key,
        long generation,
        Func<RealtimeObservationState, RealtimeObservationState> update,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IRealtimeObservationStateStore>();

        for (var attempt = 0; attempt < MaxStateWriteAttempts; attempt++)
        {
            var current = await store.GetAsync(
                    key.UserId,
                    key.ExchangeAccountId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (current is null || current.Value.Generation != generation)
            {
                return false;
            }

            try
            {
                await store.SaveAsync(
                        key.UserId,
                        update(current.Value),
                        current.Version,
                        cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
            catch (ConcurrencyConflictException) when (attempt < MaxStateWriteAttempts - 1)
            {
            }
        }

        throw new ConcurrencyConflictException(
            $"Состояние realtime-наблюдения аккаунта {key.ExchangeAccountId} изменилось за ограниченное число повторов.");
    }

    private async Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
        AccountKey key,
        RealtimeObservationTransportStatus status,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IRealtimeObservationStateStore>()
            .AdvanceGenerationAsync(
                key.UserId,
                key.ExchangeAccountId,
                status,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Versioned<RealtimeObservationState>?> ReadObservationStateAsync(
        AccountKey key,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IRealtimeObservationStateStore>()
            .GetAsync(key.UserId, key.ExchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateDegradedStateAsync(
        AccountKey key,
        long generation,
        string failureCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await UpdateCurrentStateAsync(
                    key,
                    generation,
                    current => current with
                    {
                        TransportStatus = RealtimeObservationTransportStatus.Degraded,
                        LastHeartbeatAt = null,
                        RecoveryRequired = true,
                        FailureCode = failureCode,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ExchangeRealtimeObservationLogMessages.LogStateUpdateFailed(
                logger,
                key.UserId.Value,
                key.ExchangeAccountId.Value,
                exception.GetType().FullName);
        }
    }

    private async Task<bool> DisposeProviderAsync(ActiveAccount session)
    {
        if (session.IsProviderDisposed)
        {
            return true;
        }

        if (!await DisposeProviderAsync(session.Provider, session.Key).ConfigureAwait(false))
        {
            return false;
        }

        session.MarkProviderDisposed();
        ExchangeRealtimeObservationTelemetry.RecordProviderStopped();
        ExchangeRealtimeObservationLogMessages.LogProviderStopped(
            logger,
            session.Key.UserId.Value,
            session.Key.ExchangeAccountId.Value);
        return true;
    }

    private async Task<bool> DisposeProviderAsync(
        IExchangeRealtimeProvider provider,
        AccountKey key)
    {
        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            ExchangeRealtimeObservationLogMessages.LogProviderDisposeFailed(
                logger,
                key.UserId.Value,
                key.ExchangeAccountId.Value,
                exception.GetType().FullName);
            return false;
        }
    }

    private void ScheduleAllActive()
    {
        foreach (var session in activeAccounts.Values)
        {
            Schedule(session);
        }
    }

    private void Schedule(ActiveAccount session)
    {
        if (!session.TrySchedule())
        {
            return;
        }

        if (!workQueue.Writer.TryWrite(session))
        {
            session.CancelSchedule();
        }
    }

    private void LogQueueOverflow(AccountKey key) =>
        ExchangeRealtimeObservationLogMessages.LogQueueOverflow(
            logger,
            key.UserId.Value,
            key.ExchangeAccountId.Value);

    private DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    private bool IsCurrentSession(ActiveAccount session) =>
        !session.IsStopping
        && activeAccounts.TryGetValue(session.Key, out var active)
        && ReferenceEquals(active, session);

    private void ScheduleRetry(AccountKey key)
    {
        var attempt = retries.TryGetValue(key, out var previous)
            ? previous.Attempt + 1
            : 1;
        var exponential = settings.ReconnectInitialDelay.TotalMilliseconds
            * Math.Pow(2, Math.Min(attempt - 1, 20));
        var baseDelayMs = Math.Min(exponential, settings.ReconnectMaxDelay.TotalMilliseconds);
        var jitterMs = baseDelayMs * settings.ReconnectJitter;
        var delay = TimeSpan.FromMilliseconds(
            baseDelayMs - jitterMs + Random.Shared.NextDouble() * jitterMs * 2);
        retries[key] = new RetryState(attempt, timeProvider.GetUtcNow() + delay);
    }

    private static RealtimeObservationTransportStatus MapTransportStatus(
        RealtimeTransportStatus status) =>
        status switch
        {
            RealtimeTransportStatus.Subscribed or RealtimeTransportStatus.Restored =>
                RealtimeObservationTransportStatus.Connected,
            RealtimeTransportStatus.ConnectionLost =>
                RealtimeObservationTransportStatus.Reconnecting,
            _ => RealtimeObservationTransportStatus.Degraded,
        };

    private async Task StopSessionAsync(ActiveAccount session, bool advanceGeneration, CancellationToken cancellationToken = default)
    {
        session.BeginStopping();
        var generationTransitionComplete = !advanceGeneration || session.IsGenerationFenced;
        if (advanceGeneration)
        {
            try
            {
                if (!session.IsGenerationFenced)
                {
                    var state = await AdvanceGenerationAsync(
                            session.Key,
                            RealtimeObservationTransportStatus.Stopped,
                            cancellationToken)
                        .ConfigureAwait(false);
                    session.MarkGenerationFenced(state.Value.Generation);
                    generationTransitionComplete = true;
                }
                else
                {
                    await UpdateCurrentStateAsync(
                            session.Key,
                            session.FencedGeneration,
                            current => current with
                            {
                                TransportStatus = RealtimeObservationTransportStatus.Stopped,
                                LastHeartbeatAt = null,
                                FailureCode = null,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    generationTransitionComplete = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                ExchangeRealtimeObservationLogMessages.LogStateUpdateFailed(
                    logger,
                    session.Key.UserId.Value,
                    session.Key.ExchangeAccountId.Value,
                    exception.GetType().FullName);

                if (!session.IsGenerationFenced)
                {
                    try
                    {
                        generationTransitionComplete = await ReadObservationStateAsync(
                                session.Key,
                                cancellationToken)
                            .ConfigureAwait(false) is null;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception stateException)
                    {
                        ExchangeRealtimeObservationLogMessages.LogStateUpdateFailed(
                            logger,
                            session.Key.UserId.Value,
                            session.Key.ExchangeAccountId.Value,
                            stateException.GetType().FullName);
                    }
                }
            }
        }

        if (await DisposeProviderAsync(session).ConfigureAwait(false)
            && (generationTransitionComplete || session.IsGenerationFenced))
        {
            activeAccounts.TryRemove(session.Key, out _);
            retries.Remove(session.Key);
        }
    }

    private async Task StopAllAsync(CancellationToken cancellationToken)
    {
        foreach (var session in activeAccounts.Values.ToArray())
        {
            await StopSessionAsync(session, advanceGeneration: true, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private readonly record struct AccountKey(
        UserId UserId,
        ExchangeAccountId ExchangeAccountId);

    private sealed record AccountDescriptor(
        ExchangeAccount Account,
        ConcurrencyVersion? CredentialVersion);

    private sealed record RetryState(int Attempt, DateTimeOffset NextAttemptAt);

    private sealed class ActiveAccount : IExchangeRealtimeObserver
    {
        private readonly ExchangeRealtimeObservationSupervisor _owner;
        private readonly ExchangeAccountObservationCoordinator _coordinator;
        private RealtimeTransportState? _latestTransportState;
        private long _recoveryRequestCount;
        private long _workVersion;
        private int _scheduled;
        private int _ready;
        private int _stopping;
        private int _providerDisposed;
        private int _generationFenced;
        private long _fencedGeneration;
        private int _transportConnected;

        public ActiveAccount(
            ExchangeRealtimeObservationSupervisor owner,
            ExchangeAccountObservationCoordinator coordinator,
            AccountKey key,
            long generation,
            ConcurrencyVersion credentialVersion,
            IExchangeRealtimeProvider provider)
        {
            _owner = owner;
            _coordinator = coordinator;
            Key = key;
            Generation = generation;
            CredentialVersion = credentialVersion;
            Provider = provider;
        }

        public AccountKey Key { get; }
        public long Generation { get; }
        public ConcurrencyVersion CredentialVersion { get; }
        public IExchangeRealtimeProvider Provider { get; }
        public bool IsStopping => Volatile.Read(ref _stopping) != 0;
        public bool IsProviderDisposed => Volatile.Read(ref _providerDisposed) != 0;
        public bool IsGenerationFenced => Volatile.Read(ref _generationFenced) != 0;
        public long FencedGeneration => Interlocked.Read(ref _fencedGeneration);
        public bool IsReady => Volatile.Read(ref _ready) != 0;
        public bool IsTransportConnected => Volatile.Read(ref _transportConnected) != 0;
        public void OnPosition(PrivatePositionObservation observation)
        {
            if (IsStopping)
            {
                return;
            }

            if (!_coordinator.TryEnqueue(Key.UserId, Generation, observation))
            {
                ExchangeRealtimeObservationTelemetry.RecordDroppedSignal();
                _owner.LogQueueOverflow(Key);
            }

            RequestWork();
        }

        public void OnWallet(PrivateWalletObservation observation)
        {
            if (IsStopping)
            {
                return;
            }

            if (!_coordinator.TryEnqueue(Key.UserId, Generation, observation))
            {
                ExchangeRealtimeObservationTelemetry.RecordDroppedSignal();
                _owner.LogQueueOverflow(Key);
            }

            RequestWork();
        }

        public void OnTransportState(RealtimeTransportState state)
        {
            if (IsStopping)
            {
                return;
            }

            if (state.RecoveryRequired)
            {
                Interlocked.Increment(ref _recoveryRequestCount);
            }

            Interlocked.Exchange(ref _latestTransportState, state);
            SetTransportConnected(
                state.Status is RealtimeTransportStatus.Subscribed or RealtimeTransportStatus.Restored);
            RequestWork();
        }

        public bool TrySchedule()
        {
            if (!IsReady || IsStopping)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0;
        }

        public void CancelSchedule() => Interlocked.Exchange(ref _scheduled, 0);

        public void MarkReady()
        {
            Volatile.Write(ref _ready, 1);
            RequestWork();
        }

        public void BeginStopping()
        {
            Interlocked.Exchange(ref _stopping, 1);
            SetTransportConnected(connected: false);
        }

        public void MarkProviderDisposed() => Volatile.Write(ref _providerDisposed, 1);

        public void MarkGenerationFenced(long generation)
        {
            Interlocked.Exchange(ref _fencedGeneration, generation);
            Interlocked.Exchange(ref _generationFenced, 1);
        }

        public void SetTransportConnected(bool connected) =>
            Volatile.Write(ref _transportConnected, connected ? 1 : 0);

        public void RequestWork()
        {
            Interlocked.Increment(ref _workVersion);
            _owner.Schedule(this);
        }

        public long MarkDequeued() => Interlocked.Read(ref _workVersion);

        public void MarkProcessed(long observedWorkVersion)
        {
            Interlocked.Exchange(ref _scheduled, 0);
            if (Interlocked.Read(ref _workVersion) != observedWorkVersion)
            {
                _owner.Schedule(this);
            }
        }

        public (RealtimeTransportState? State, long RecoveryRequestCount) TakeTransportState()
        {
            var recoveryRequestCount = Interlocked.Exchange(ref _recoveryRequestCount, 0);
            var state = Interlocked.Exchange(ref _latestTransportState, null);
            if (state is null && recoveryRequestCount > 0)
            {
                state = new RealtimeTransportState(
                    RealtimeTransportStatus.MessageRejected,
                    _owner.GetUtcNow());
            }

            return (state, recoveryRequestCount);
        }
    }
}
