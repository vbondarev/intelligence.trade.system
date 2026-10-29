using System.Threading.Channels;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
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

    private sealed class TrackingRealtimeProviderFactory : IExchangeRealtimeProviderFactory
    {
        private readonly Channel<TrackingRealtimeProvider> created =
            Channel.CreateUnbounded<TrackingRealtimeProvider>();

        public IExchangeRealtimeProvider Create(
            ExchangeId exchange,
            ExchangeAccountId exchangeAccountId,
            ExchangeAccountCredential credentials)
        {
            Assert.Equal(ExchangeId.Bybit, exchange);
            var provider = new TrackingRealtimeProvider(exchangeAccountId, credentials.Version);
            Assert.True(created.Writer.TryWrite(provider));
            return provider;
        }

        public Task<TrackingRealtimeProvider> ReadCreatedAsync() =>
            created.Reader.ReadAsync().AsTask().WaitAsync(CompletionTimeout);
    }

    private sealed class TrackingRealtimeProvider(
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion credentialVersion) : IExchangeRealtimeProvider
    {
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

        public Task<RealtimeTransportState> StartAsync(
            IExchangeRealtimeObserver observer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = new RealtimeTransportState(RealtimeTransportStatus.Subscribed, T1);
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
}
