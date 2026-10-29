using System.Threading.Channels;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Realtime;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Supervisor_replaces_provider_after_credential_rotation_and_fences_old_generation()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var applicationClock = new MutableTimeProvider(T1.AddMinutes(1));
        var supervisorClock = new FixedTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = applicationClock };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            applicationClock,
            realtimeProviderFactory: realtimeProviderFactory);
        var credentialStore = services.GetRequiredService<IExchangeAccountCredentialStore>()
            as TestCredentialStore;
        Assert.NotNull(credentialStore);

        var options = new ExchangeRealtimeObservationOptions
        {
            QueueCapacity = 8,
            AccountConsumerCount = 2,
            AccountDiscoveryInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            HeartbeatStaleAfter = TimeSpan.FromSeconds(1),
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            ReconnectJitter = 0,
        };
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            applicationClock,
            options.QueueCapacity);
        var supervisor = new ExchangeRealtimeObservationSupervisor(
            services.GetRequiredService<IServiceScopeFactory>(),
            realtimeProviderFactory,
            coordinator,
            Options.Create(options),
            supervisorClock,
            NullLogger<ExchangeRealtimeObservationSupervisor>.Instance);
        using var cancellation = new CancellationTokenSource();
        var run = supervisor.RunAsync(cancellation.Token);

        TrackingRealtimeProvider? firstProvider = null;
        TrackingRealtimeProvider? rotatedProvider = null;
        try
        {
            firstProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Started.Task.WaitAsync(CompletionTimeout);
            var firstState = await WaitForObservationStateAsync(
                account,
                state => state.Generation >= 2
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            applicationClock.UtcNow = T1.AddMinutes(2);
            firstProvider.FailNextDispose();
            credentialStore.Version = new ConcurrencyVersion(2);
            rotatedProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Disposed.Task.WaitAsync(CompletionTimeout);
            await rotatedProvider.Started.Task.WaitAsync(CompletionTimeout);

            var rotatedState = await WaitForObservationStateAsync(
                account,
                state => state.Generation == firstState.Generation + 1
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            Assert.Equal(new ConcurrencyVersion(2), rotatedProvider.CredentialVersion);
            Assert.Equal(2, firstProvider.DisposeAttempts);
            Assert.Equal(firstState.Generation + 1, rotatedState.Generation);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }

        Assert.NotNull(rotatedProvider);
        await rotatedProvider.Disposed.Task.WaitAsync(CompletionTimeout);
        var stoppedState = await ReadObservationStateAsync(account);
        Assert.Equal(RealtimeObservationTransportStatus.Stopped, stoppedState.TransportStatus);
        Assert.True(stoppedState.Generation > 2);
    }

    [Fact]
    public async Task Supervisor_retries_generation_fence_before_dropping_stopped_session()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var stateStoreFailure = new GenerationAdvanceFailureGate();
        var restProvider = new TestPrivateProvider();
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailOnceGenerationAdvanceStateStore(store, stateStoreFailure),
            realtimeProviderFactory: realtimeProviderFactory);

        var options = new ExchangeRealtimeObservationOptions
        {
            QueueCapacity = 8,
            AccountConsumerCount = 1,
            AccountDiscoveryInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            HeartbeatStaleAfter = TimeSpan.FromSeconds(1),
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            ReconnectJitter = 0,
        };
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            options.QueueCapacity);
        var supervisor = new ExchangeRealtimeObservationSupervisor(
            services.GetRequiredService<IServiceScopeFactory>(),
            realtimeProviderFactory,
            coordinator,
            Options.Create(options),
            clock,
            NullLogger<ExchangeRealtimeObservationSupervisor>.Instance);
        using var cancellation = new CancellationTokenSource();
        var run = supervisor.RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            stateStoreFailure.FailNextAdvance();
            await using (var scope = services.CreateAsyncScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IExchangeAccountRepository>();
                var current = await repository.GetByIdAsync(account.UserId, account.Id);
                Assert.NotNull(current);
                current.Value.Disable();
                await repository.SaveAsync(account.UserId, current.Value, current.Version);
            }

            var stoppedState = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Stopped);

            Assert.Equal(1, stateStoreFailure.FailuresObserved);
            Assert.Equal(1, provider.DisposeAttempts);
            Assert.True(stoppedState.Generation > 1);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_removes_stopped_session_when_generation_state_is_gone()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        var stateStoreFailure = new GenerationAdvanceFailureGate();
        var restProvider = new TestPrivateProvider();
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailOnceGenerationAdvanceStateStore(store, stateStoreFailure),
            realtimeProviderFactory: realtimeProviderFactory);

        var options = new ExchangeRealtimeObservationOptions
        {
            QueueCapacity = 8,
            AccountConsumerCount = 1,
            AccountDiscoveryInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            HeartbeatStaleAfter = TimeSpan.FromSeconds(1),
            ReconnectInitialDelay = TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(50),
            ReconnectJitter = 0,
        };
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            options.QueueCapacity);
        var supervisor = new ExchangeRealtimeObservationSupervisor(
            services.GetRequiredService<IServiceScopeFactory>(),
            realtimeProviderFactory,
            coordinator,
            Options.Create(options),
            clock,
            NullLogger<ExchangeRealtimeObservationSupervisor>.Instance);
        using var cancellation = new CancellationTokenSource();
        var run = supervisor.RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            stateStoreFailure.FailNextAdvance();
            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TradeSystemDbContext>();
                var repository = scope.ServiceProvider.GetRequiredService<IExchangeAccountRepository>();
                await using var transaction = await context.Database.BeginTransactionAsync();
                var current = await repository.GetByIdAsync(account.UserId, account.Id);
                Assert.NotNull(current);
                current.Value.Disable();
                await repository.SaveAsync(account.UserId, current.Value, current.Version);
                var deletedStateCount = await context.ExchangeRealtimeObservations
                    .Where(state => state.ExchangeAccountId == account.Id.Value)
                    .ExecuteDeleteAsync();
                Assert.Equal(1, deletedStateCount);
                await transaction.CommitAsync();
            }

            await provider.Disposed.Task.WaitAsync(CompletionTimeout);
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            Assert.Equal(1, stateStoreFailure.FailuresObserved);
            Assert.Equal(2, stateStoreFailure.AdvanceCalls);
            Assert.Equal(1, provider.DisposeAttempts);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_retries_restored_recovery_after_postgres_save_failure_without_duplicate_epoch()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var saveGate = new TransportStateSaveGate(
            failureCount: 1,
            static () => new NpgsqlException("Искусственная ошибка PostgreSQL."),
            holdFirstFailure: true);
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailingTransportStateStore(store, saveGate),
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            var initialPositionRequests = restProvider.FullPositionRequestCount;

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            await saveGate.FirstSaveEntered.Task.WaitAsync(CompletionTimeout);

            var beforeRetry = await ReadObservationStateAsync(account);
            Assert.Equal(initial.Generation, beforeRetry.Generation);
            Assert.False(beforeRetry.RecoveryRequired);
            Assert.Equal(initial.RecoveryRequestEpoch, beforeRetry.RecoveryRequestEpoch);

            saveGate.ReleaseFirstFailure();
            var recovered = await WaitForObservationStateAsync(
                account,
                state => state.Generation == initial.Generation
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 1
                    && !state.RecoveryRequired);

            Assert.Equal(restProvider.ObservedAt, recovered.LastFullyReconciledAt);
            Assert.Equal(initialPositionRequests + 1, restProvider.FullPositionRequestCount);

            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 2);
            var afterAdditionalPasses = await ReadObservationStateAsync(account);
            Assert.Equal(initial.Generation, afterAdditionalPasses.Generation);
            Assert.Equal(initial.RecoveryRequestEpoch + 1, afterAdditionalPasses.RecoveryRequestEpoch);
            Assert.Equal(initialPositionRequests + 1, restProvider.FullPositionRequestCount);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
            Assert.Equal(0, provider.DisposeAttempts);
        }
        finally
        {
            saveGate.ReleaseFirstFailure();
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_retries_recovery_after_exhausted_postgres_cas_attempts_without_duplicate_epoch()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var saveGate = new TransportStateSaveGate(
            failureCount: 3,
            static () => new ConcurrencyConflictException("Искусственный CAS-конфликт."));
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailingTransportStateStore(store, saveGate),
            realtimeProviderFactory: realtimeProviderFactory);
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            var initialPositionRequests = restProvider.FullPositionRequestCount;

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());

            var recovered = await WaitForObservationStateAsync(
                account,
                state => state.Generation == initial.Generation
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 1
                    && !state.RecoveryRequired);

            Assert.Equal(3, saveGate.FailuresObserved);
            Assert.Equal(restProvider.ObservedAt, recovered.LastFullyReconciledAt);
            Assert.Equal(initialPositionRequests + 1, restProvider.FullPositionRequestCount);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_preserves_new_recovery_signal_arriving_during_failed_persistence_retry()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var saveGate = new TransportStateSaveGate(
            failureCount: 1,
            static () => new NpgsqlException("Искусственная ошибка PostgreSQL."),
            holdFirstFailure: true);
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailingTransportStateStore(store, saveGate),
            realtimeProviderFactory: realtimeProviderFactory);
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            var initialPositionRequests = restProvider.FullPositionRequestCount;

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            await saveGate.FirstSaveEntered.Task.WaitAsync(CompletionTimeout);

            clock.UtcNow = T1.AddMinutes(3);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.ConnectionLost, clock.GetUtcNow());
            saveGate.ReleaseFirstFailure();

            var recovered = await WaitForObservationStateAsync(
                account,
                state => state.Generation == initial.Generation
                    && state.TransportStatus == RealtimeObservationTransportStatus.Reconnecting
                    && state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 2
                    && !state.RecoveryRequired);

            Assert.Equal(restProvider.ObservedAt, recovered.LastFullyReconciledAt);
            Assert.Equal(initialPositionRequests + 1, restProvider.FullPositionRequestCount);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
        }
        finally
        {
            saveGate.ReleaseFirstFailure();
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_rest_recovery_does_not_confirm_a_later_transport_request()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var firstRecoveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRecoveryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var positionRequestCount = 0;
        restProvider.BeforePositionsResponse = async () =>
        {
            switch (Interlocked.Increment(ref positionRequestCount))
            {
                case 2:
                    firstRecoveryEntered.TrySetResult();
                    await releaseFirstRecovery.Task;
                    break;
                case 3:
                    secondRecoveryEntered.TrySetResult();
                    await releaseSecondRecovery.Task;
                    break;
            }
        };

        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            await firstRecoveryEntered.Task.WaitAsync(CompletionTimeout);

            var stateDuringFirstRecovery = await ReadObservationStateAsync(account);
            Assert.Equal(initial.RecoveryRequestEpoch + 1, stateDuringFirstRecovery.RecoveryRequestEpoch);
            Assert.True(stateDuringFirstRecovery.RecoveryRequired);

            clock.UtcNow = T1.AddMinutes(3);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.ConnectionLost, clock.GetUtcNow());
            releaseFirstRecovery.TrySetResult();

            await secondRecoveryEntered.Task.WaitAsync(CompletionTimeout);
            var stateDuringSecondRecovery = await ReadObservationStateAsync(account);
            Assert.Equal(initial.RecoveryRequestEpoch + 2, stateDuringSecondRecovery.RecoveryRequestEpoch);
            Assert.Equal(RealtimeObservationTransportStatus.Reconnecting, stateDuringSecondRecovery.TransportStatus);
            Assert.True(stateDuringSecondRecovery.RecoveryRequired);

            releaseSecondRecovery.TrySetResult();
            var recovered = await WaitForObservationStateAsync(
                account,
                state => state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 2
                    && state.TransportStatus == RealtimeObservationTransportStatus.Reconnecting
                    && !state.RecoveryRequired);

            Assert.Equal(restProvider.ObservedAt, recovered.LastFullyReconciledAt);
            Assert.Equal(3, restProvider.FullPositionRequestCount);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
        }
        finally
        {
            releaseFirstRecovery.TrySetResult();
            releaseSecondRecovery.TrySetResult();
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_does_not_apply_pending_transport_state_after_generation_changes()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var saveGate = new TransportStateSaveGate(
            failureCount: 1,
            static () => new NpgsqlException("Искусственная ошибка PostgreSQL."),
            holdFirstFailure: true);
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            decorateStateStore: store => new FailingTransportStateStore(store, saveGate),
            realtimeProviderFactory: realtimeProviderFactory);
        var options = CreateSupervisorOptions(accountDiscoveryInterval: TimeSpan.FromMinutes(10));
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            clock.UtcNow = T1.AddMinutes(2);
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            await saveGate.FirstSaveEntered.Task.WaitAsync(CompletionTimeout);

            await using var context = fixture.CreateContext();
            var fencedState = await new RealtimeObservationStateStore(context).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Degraded);
            Assert.Equal(initial.Generation + 1, fencedState.Value.Generation);

            saveGate.TrackNextStateRead();
            saveGate.ReleaseFirstFailure();
            provider.Report(RealtimeTransportStatus.MessageRejected, clock.GetUtcNow());

            var stateReadByRetry = await saveGate.TrackedStateRead.WaitAsync(CompletionTimeout);
            Assert.NotNull(stateReadByRetry);
            Assert.Equal(fencedState.Value.Generation, stateReadByRetry.Value.Generation);

            var current = await ReadObservationStateAsync(account);
            Assert.Equal(fencedState.Value, current);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
        }
        finally
        {
            saveGate.ReleaseFirstFailure();
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_keeps_healthy_and_sdk_reconnecting_session_and_runs_rest_recovery_after_restored()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var provider = await realtimeProviderFactory.ReadCreatedAsync();
            await provider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            Assert.True(initial.IsTransportOperational(clock.GetUtcNow(), options.HeartbeatStaleAfter));

            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 2);
            var stillHealthy = await ReadObservationStateAsync(account);
            Assert.Equal(initial.Generation, stillHealthy.Generation);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, restProvider.FullPositionRequestCount);

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.ConnectionLost, clock.GetUtcNow());
            var reconnecting = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Reconnecting
                    && state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 1
                    && !state.RecoveryRequired);

            discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);
            var stillReconnecting = await ReadObservationStateAsync(account);
            Assert.Equal(initial.Generation, stillReconnecting.Generation);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
            Assert.Equal(0, provider.DisposeAttempts);

            clock.UtcNow = T1.AddMinutes(3);
            restProvider.ObservedAt = clock.GetUtcNow();
            provider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            var restored = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && state.RecoveryRequestEpoch == reconnecting.RecoveryRequestEpoch + 1
                    && !state.RecoveryRequired);

            Assert.Equal(initial.Generation, restored.Generation);
            Assert.Equal(restProvider.ObservedAt, restored.LastFullyReconciledAt);
            Assert.Equal(3, restProvider.FullPositionRequestCount);

            discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 2);
            var afterAdditionalPasses = await ReadObservationStateAsync(account);
            Assert.Equal(initial.Generation, afterAdditionalPasses.Generation);
            Assert.Equal(restored.RecoveryRequestEpoch, afterAdditionalPasses.RecoveryRequestEpoch);
            Assert.Equal(3, restProvider.FullPositionRequestCount);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
            Assert.Equal(0, provider.DisposeAttempts);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_restarts_connected_session_with_stale_heartbeat()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions(
            heartbeatInterval: TimeSpan.FromMinutes(5),
            heartbeatStaleAfter: TimeSpan.FromMinutes(6));
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var firstProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);
            var positionRequestsBeforeRestart = restProvider.FullPositionRequestCount;

            await using (var context = fixture.CreateContext())
            {
                var store = new RealtimeObservationStateStore(context);
                var current = await store.GetAsync(account.UserId, account.Id);
                Assert.NotNull(current);
                await store.SaveAsync(
                    account.UserId,
                    current.Value with
                    {
                        LastHeartbeatAt = clock.GetUtcNow()
                            - options.HeartbeatStaleAfter
                            - TimeSpan.FromSeconds(1),
                    },
                    current.Version);
            }

            clock.UtcNow += TimeSpan.FromSeconds(1);
            restProvider.ObservedAt = clock.GetUtcNow();
            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);
            var replacement = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Disposed.Task.WaitAsync(CompletionTimeout);
            await replacement.Started.Task.WaitAsync(CompletionTimeout);
            var restarted = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            Assert.Equal(initial.Generation + 1, restarted.Generation);
            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, firstProvider.DisposeAttempts);
            Assert.Equal(positionRequestsBeforeRestart + 1, restProvider.FullPositionRequestCount);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_restarts_reconnecting_session_after_heartbeat_staleness_window()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions(
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatStaleAfter: TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var firstProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            clock.UtcNow += TimeSpan.FromMinutes(1);
            firstProvider.Report(RealtimeTransportStatus.ConnectionLost, clock.GetUtcNow());
            await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Reconnecting
                    && !state.RecoveryRequired);
            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);
            Assert.Equal(1, realtimeProviderFactory.CreatedCount);
            Assert.Equal(0, firstProvider.DisposeAttempts);

            clock.UtcNow += options.HeartbeatStaleAfter + TimeSpan.FromSeconds(1);
            restProvider.ObservedAt = clock.GetUtcNow();
            discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);

            var replacement = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Disposed.Task.WaitAsync(CompletionTimeout);
            await replacement.Started.Task.WaitAsync(CompletionTimeout);
            var restarted = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            Assert.Equal(initial.Generation + 1, restarted.Generation);
            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, firstProvider.DisposeAttempts);
            Assert.Equal(3, restProvider.FullPositionRequestCount);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_restarts_degraded_session_after_rest_recovery()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions(accountDiscoveryInterval: TimeSpan.FromSeconds(1));
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var firstProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            firstProvider.Report(RealtimeTransportStatus.MessageRejected, clock.GetUtcNow());
            await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Degraded
                    && state.RecoveryRequestEpoch == initial.RecoveryRequestEpoch + 1
                    && !state.RecoveryRequired);
            clock.UtcNow = T1.AddMinutes(3);
            restProvider.ObservedAt = clock.GetUtcNow();
            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);

            var replacement = await realtimeProviderFactory.ReadCreatedAsync();
            await firstProvider.Disposed.Task.WaitAsync(CompletionTimeout);
            await replacement.Started.Task.WaitAsync(CompletionTimeout);
            var restarted = await WaitForObservationStateAsync(
                account,
                state => state.Generation == initial.Generation + 1
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            Assert.Equal(initial.RecoveryRequestEpoch + 2, restarted.RecoveryRequestEpoch);
            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, firstProvider.DisposeAttempts);
            Assert.Equal(3, restProvider.FullPositionRequestCount);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_retries_provider_after_temporary_subscription_failure()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        realtimeProviderFactory.FailNextStart(RealtimeTransportStatus.SubscribeFailed);
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var failedProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await failedProvider.Started.Task.WaitAsync(CompletionTimeout);
            var failedState = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Degraded
                    && state.RecoveryRequired);
            await failedProvider.Disposed.Task.WaitAsync(CompletionTimeout);

            clock.UtcNow += TimeSpan.FromSeconds(1);
            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);
            var recoveredProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await recoveredProvider.Started.Task.WaitAsync(CompletionTimeout);
            var recovered = await WaitForObservationStateAsync(
                account,
                state => state.Generation == failedState.Generation + 1
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, failedProvider.DisposeAttempts);
            Assert.Equal(1, restProvider.FullPositionRequestCount);
            Assert.Equal(failedState.RecoveryRequestEpoch + 1, recovered.RecoveryRequestEpoch);

            discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 2);
            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    [Fact]
    public async Task Supervisor_fences_old_provider_callback_when_credentials_rotate_during_reconnect()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddMinutes(1));
        var restProvider = new TestPrivateProvider { Clock = clock, ObservedAt = clock.GetUtcNow() };
        var realtimeProviderFactory = new TrackingRealtimeProviderFactory();
        await using var services = CreateApplicationServices(
            account,
            restProvider,
            clock,
            realtimeProviderFactory: realtimeProviderFactory);
        var credentialStore = Assert.IsType<TestCredentialStore>(
            services.GetRequiredService<IExchangeAccountCredentialStore>());
        var candidateSource = Assert.IsType<SingleCandidateSource>(
            services.GetRequiredService<IExchangeAccountSyncCandidateSource>());
        var options = CreateSupervisorOptions();
        using var cancellation = new CancellationTokenSource();
        var run = CreateSupervisor(services, realtimeProviderFactory, clock, options)
            .RunAsync(cancellation.Token);

        try
        {
            var oldProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await oldProvider.Started.Task.WaitAsync(CompletionTimeout);
            var initial = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            clock.UtcNow = T1.AddMinutes(2);
            restProvider.ObservedAt = clock.GetUtcNow();
            oldProvider.Report(RealtimeTransportStatus.ConnectionLost, clock.GetUtcNow());
            var reconnecting = await WaitForObservationStateAsync(
                account,
                state => state.TransportStatus == RealtimeObservationTransportStatus.Reconnecting
                    && !state.RecoveryRequired);

            clock.UtcNow += TimeSpan.FromMinutes(1);
            credentialStore.Version = new ConcurrencyVersion(2);
            var discoveryCount = candidateSource.DiscoveryPassCount;
            await candidateSource.WaitForDiscoveryCountAsync(discoveryCount + 1);
            var rotatedProvider = await realtimeProviderFactory.ReadCreatedAsync();
            await oldProvider.Disposed.Task.WaitAsync(CompletionTimeout);
            await rotatedProvider.Started.Task.WaitAsync(CompletionTimeout);
            var rotated = await WaitForObservationStateAsync(
                account,
                state => state.Generation == initial.Generation + 1
                    && state.TransportStatus == RealtimeObservationTransportStatus.Connected
                    && !state.RecoveryRequired);

            Assert.Equal(new ConcurrencyVersion(2), rotatedProvider.CredentialVersion);
            Assert.Equal(reconnecting.RecoveryRequestEpoch + 1, rotated.RecoveryRequestEpoch);
            Assert.Equal(2, realtimeProviderFactory.CreatedCount);
            Assert.Equal(1, oldProvider.DisposeAttempts);
            Assert.Equal(3, restProvider.FullPositionRequestCount);

            oldProvider.Report(RealtimeTransportStatus.Restored, clock.GetUtcNow());
            var afterOldCallback = await ReadObservationStateAsync(account);
            Assert.Equal(rotated, afterOldCallback);
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(CompletionTimeout);
        }
    }

    private async Task<RealtimeObservationState> WaitForObservationStateAsync(
        ExchangeAccount account,
        Func<RealtimeObservationState, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(CompletionTimeout);
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var state = await ReadObservationStateAsync(account, timeout.Token);
                if (predicate(state))
                {
                    return state;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            var state = await ReadObservationStateAsync(account);
            throw new TimeoutException(
                $"Истекло ожидание состояния realtime-наблюдения. Поколение={state.Generation}, "
                + $"Status={state.TransportStatus}, RecoveryRequired={state.RecoveryRequired}, "
                + $"FailureCode={state.FailureCode}, RecoveryRequestEpoch={state.RecoveryRequestEpoch}.");
        }
    }

    private async Task<RealtimeObservationState> ReadObservationStateAsync(
        ExchangeAccount account,
        CancellationToken cancellationToken = default)
    {
        await using var context = fixture.CreateContext();
        var state = await new RealtimeObservationStateStore(context)
            .GetAsync(account.UserId, account.Id, cancellationToken);
        Assert.NotNull(state);
        return state.Value;
    }

    private static ExchangeRealtimeObservationOptions CreateSupervisorOptions(
        TimeSpan? accountDiscoveryInterval = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatStaleAfter = null,
        TimeSpan? reconnectInitialDelay = null) =>
        new()
        {
            QueueCapacity = 8,
            AccountConsumerCount = 1,
            AccountDiscoveryInterval = accountDiscoveryInterval ?? TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = heartbeatInterval ?? TimeSpan.FromMinutes(5),
            HeartbeatStaleAfter = heartbeatStaleAfter ?? TimeSpan.FromMinutes(6),
            ReconnectInitialDelay = reconnectInitialDelay ?? TimeSpan.FromMilliseconds(10),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(100),
            ReconnectJitter = 0,
        };

    private static ExchangeRealtimeObservationSupervisor CreateSupervisor(
        ServiceProvider services,
        TrackingRealtimeProviderFactory realtimeProviderFactory,
        TimeProvider clock,
        ExchangeRealtimeObservationOptions options)
    {
        var coordinator = new ExchangeAccountObservationCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            options.QueueCapacity);
        return new ExchangeRealtimeObservationSupervisor(
            services.GetRequiredService<IServiceScopeFactory>(),
            realtimeProviderFactory,
            coordinator,
            Options.Create(options),
            clock,
            NullLogger<ExchangeRealtimeObservationSupervisor>.Instance);
    }

    private sealed class TrackingRealtimeProviderFactory : IExchangeRealtimeProviderFactory
    {
        private readonly Channel<TrackingRealtimeProvider> created =
            Channel.CreateUnbounded<TrackingRealtimeProvider>();
        private int createdCount;
        private int nextStartStatus = (int)RealtimeTransportStatus.Subscribed;

        public int CreatedCount => Volatile.Read(ref createdCount);

        public void FailNextStart(RealtimeTransportStatus status) =>
            Interlocked.Exchange(ref nextStartStatus, (int)status);

        public IExchangeRealtimeProvider Create(
            ExchangeId exchange,
            ExchangeAccountId exchangeAccountId,
            ExchangeAccountCredential credentials)
        {
            Assert.Equal(ExchangeId.Bybit, exchange);
            var status = (RealtimeTransportStatus)Interlocked.Exchange(
                ref nextStartStatus,
                (int)RealtimeTransportStatus.Subscribed);
            var provider = new TrackingRealtimeProvider(exchangeAccountId, credentials.Version, status);
            Interlocked.Increment(ref createdCount);
            Assert.True(created.Writer.TryWrite(provider));
            return provider;
        }

        public Task<TrackingRealtimeProvider> ReadCreatedAsync() =>
            created.Reader.ReadAsync().AsTask().WaitAsync(CompletionTimeout);
    }

    private sealed class TrackingRealtimeProvider(
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion credentialVersion,
        RealtimeTransportStatus startStatus) : IExchangeRealtimeProvider
    {
        private IExchangeRealtimeObserver? observer;

        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int failNextDispose;
        private int disposeAttempts;

        public ExchangeAccountId ExchangeAccountId => exchangeAccountId;
        public ConcurrencyVersion CredentialVersion => credentialVersion;
        public int DisposeAttempts => Volatile.Read(ref disposeAttempts);

        public void FailNextDispose() => Interlocked.Exchange(ref failNextDispose, 1);

        public void Report(RealtimeTransportStatus status, DateTimeOffset occurredAt) =>
            Volatile.Read(ref observer)?.OnTransportState(new RealtimeTransportState(status, occurredAt));

        public Task<RealtimeTransportState> StartAsync(
            IExchangeRealtimeObserver observer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref this.observer, observer);
            var failure = startStatus == RealtimeTransportStatus.Subscribed
                ? null
                : new ExchangeFailure(ExchangeFailureKind.Unavailable, Retryable: true);
            var state = new RealtimeTransportState(startStatus, T1, failure);
            observer.OnTransportState(state);
            Started.TrySetResult(true);
            return Task.FromResult(state);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposeAttempts);
            if (Interlocked.Exchange(ref failNextDispose, 0) != 0)
            {
                return ValueTask.FromException(new InvalidOperationException("Искусственный сбой освобождения провайдера."));
            }

            Disposed.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GenerationAdvanceFailureGate
    {
        private int failNext;
        private int advanceCalls;
        private int failuresObserved;

        public int AdvanceCalls => Volatile.Read(ref advanceCalls);
        public int FailuresObserved => Volatile.Read(ref failuresObserved);

        public void FailNextAdvance() => Interlocked.Exchange(ref failNext, 1);

        public void RecordAdvance() => Interlocked.Increment(ref advanceCalls);

        public bool TryFailAdvance()
        {
            if (Interlocked.Exchange(ref failNext, 0) == 0)
            {
                return false;
            }

            Interlocked.Increment(ref failuresObserved);
            return true;
        }
    }

    private sealed class FailOnceGenerationAdvanceStateStore(
        IRealtimeObservationStateStore inner,
        GenerationAdvanceFailureGate failureGate) : IRealtimeObservationStateStore
    {
        public Task<Versioned<RealtimeObservationState>?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.GetAsync(userId, exchangeAccountId, cancellationToken);

        public Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            RealtimeObservationTransportStatus status,
            CancellationToken cancellationToken = default)
        {
            failureGate.RecordAdvance();
            return failureGate.TryFailAdvance()
                ? Task.FromException<Versioned<RealtimeObservationState>>(
                    new InvalidOperationException("Искусственный сбой смены поколения."))
                : inner.AdvanceGenerationAsync(userId, exchangeAccountId, status, cancellationToken);
        }

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            RealtimeObservationState state,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(userId, state, expectedVersion, cancellationToken);

        public Task<RealtimeObservationStateLockResult> TryLockGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            long generation,
            CancellationToken cancellationToken = default) =>
            inner.TryLockGenerationAsync(userId, exchangeAccountId, generation, cancellationToken);
    }

    private sealed class TransportStateSaveGate(
        int failureCount,
        Func<Exception> exceptionFactory,
        bool holdFirstFailure = false)
    {
        private int failuresRemaining = failureCount;
        private int failuresObserved;
        private int trackNextStateRead;
        private readonly TaskCompletionSource<Versioned<RealtimeObservationState>?> trackedStateRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstSaveEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource ReleaseFirstSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Versioned<RealtimeObservationState>?> TrackedStateRead => trackedStateRead.Task;
        public int FailuresObserved => Volatile.Read(ref failuresObserved);

        public void ReleaseFirstFailure() => ReleaseFirstSave.TrySetResult();

        public void TrackNextStateRead() => Interlocked.Exchange(ref trackNextStateRead, 1);

        public void ObserveStateRead(Versioned<RealtimeObservationState>? state)
        {
            if (Interlocked.Exchange(ref trackNextStateRead, 0) != 0)
            {
                trackedStateRead.TrySetResult(state);
            }
        }

        public async Task ThrowIfConfiguredAsync(
            RealtimeObservationState state,
            CancellationToken cancellationToken)
        {
            if (state.FailureCode is not ("Restored" or "ConnectionLost" or "MessageRejected")
                || !TryConsumeFailure())
            {
                return;
            }

            var observed = Interlocked.Increment(ref failuresObserved);
            if (observed == 1)
            {
                FirstSaveEntered.TrySetResult();
                if (holdFirstFailure)
                {
                    await ReleaseFirstSave.Task.WaitAsync(cancellationToken);
                }
            }

            throw exceptionFactory();
        }

        private bool TryConsumeFailure()
        {
            while (true)
            {
                var current = Volatile.Read(ref failuresRemaining);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref failuresRemaining, current - 1, current) == current)
                {
                    return true;
                }
            }
        }
    }

    private sealed class FailingTransportStateStore(
        IRealtimeObservationStateStore inner,
        TransportStateSaveGate saveGate) : IRealtimeObservationStateStore
    {
        public async Task<Versioned<RealtimeObservationState>?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            var state = await inner.GetAsync(userId, exchangeAccountId, cancellationToken);
            saveGate.ObserveStateRead(state);
            return state;
        }

        public Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            RealtimeObservationTransportStatus status,
            CancellationToken cancellationToken = default) =>
            inner.AdvanceGenerationAsync(userId, exchangeAccountId, status, cancellationToken);

        public async Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            RealtimeObservationState state,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default)
        {
            await saveGate.ThrowIfConfiguredAsync(state, cancellationToken);
            return await inner.SaveAsync(userId, state, expectedVersion, cancellationToken);
        }

        public Task<RealtimeObservationStateLockResult> TryLockGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            long generation,
            CancellationToken cancellationToken = default) =>
            inner.TryLockGenerationAsync(userId, exchangeAccountId, generation, cancellationToken);
    }
}
