using System.Globalization;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Position_observation_is_rejected_when_another_instance_persisted_recovery()
    {
        var (account, generation) = await CreateFullyReconciledAccountAsync();
        var recovery = await PersistNewRecoveryRequestAsync(account);

        Assert.True(recovery.RecoveryRequired);
        Assert.Equal(2, recovery.RecoveryRequestEpoch);

        await using var instanceB = CreateApplicationServices(account, new TestPrivateProvider());
        var coordinator = instanceB.GetRequiredService<ExchangeAccountObservationCoordinator>();
        Assert.True(coordinator.TryEnqueue(
            account.UserId,
            generation,
            OpenObservation(account, T1.AddSeconds(1), 1)));
        var result = Assert.Single(await coordinator.ProcessPendingAsync(account.UserId, account.Id));
        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.PersistedRecoveryRequired, result.ReasonCode);

        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(0, await verification.PositionChanges.CountAsync(change =>
            verification.Positions.Any(position =>
                position.Id == change.PositionId &&
                position.ExchangeAccountId == account.Id.Value)));
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));

        var persisted = await new RealtimeObservationStateStore(verification)
            .GetAsync(account.UserId, account.Id);
        Assert.True(persisted!.Value.RecoveryRequired);
        Assert.Equal(recovery.RecoveryRequestEpoch, persisted.Value.RecoveryRequestEpoch);
    }

    [Fact]
    public async Task Wallet_observation_is_rejected_when_another_instance_persisted_recovery()
    {
        var (account, generation) = await CreateFullyReconciledAccountAsync();
        var recovery = await PersistNewRecoveryRequestAsync(account);

        Assert.True(recovery.RecoveryRequired);
        Assert.Equal(2, recovery.RecoveryRequestEpoch);

        await using (var instanceB = fixture.CreateContext())
        {
            var result = await CreateWalletService(instanceB)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1.AddSeconds(1)));

            Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
            Assert.Equal(RealtimeObservationApplyResult.PersistedRecoveryRequired, result.ReasonCode);
        }

        await using var verification = fixture.CreateContext();
        var persistedAccount = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(persistedAccount);
        Assert.Null(persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Null(await new WalletObservationCursorStore(verification)
            .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
        Assert.Null(await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id));
        Assert.Equal(0, await CountOutboxAsync(verification, account));

        var persisted = await new RealtimeObservationStateStore(verification)
            .GetAsync(account.UserId, account.Id);
        Assert.True(persisted!.Value.RecoveryRequired);
        Assert.Equal(recovery.RecoveryRequestEpoch, persisted.Value.RecoveryRequestEpoch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_commit_waits_for_a_same_generation_stream_and_fences_it(bool wallet)
    {
        var (account, generation) = await CreateFullyReconciledAccountAsync();
        await using var recoveryContext = fixture.CreateContext();
        var recoveryStore = new RealtimeObservationStateStore(recoveryContext);
        var current = await recoveryStore.GetAsync(account.UserId, account.Id);
        Assert.NotNull(current);

        await using var recoveryTransaction = await recoveryContext.Database.BeginTransactionAsync();
        var recovery = current!.Value with
        {
            RecoveryRequired = true,
            RecoveryRequestEpoch = current.Value.RecoveryRequestEpoch + 1,
        };
        await recoveryStore.SaveAsync(account.UserId, recovery, current.Version);

        await using var observerContext = fixture.CreateContext();
        var observerPid = await GetBackendPidAsync(observerContext);
        var accountLockAcquired = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transaction = new SignalAfterAccountLockTransaction(
            new ExchangeAccountSyncTransaction(observerContext),
            accountLockAcquired);
        var apply = ApplyStreamAsync(account, generation, wallet, observerContext, transaction);

        try
        {
            await accountLockAcquired.Task.WaitAsync(CompletionTimeout);
            await WaitForPostgresLockWaitAsync(observerPid);
        }
        finally
        {
            await recoveryTransaction.CommitAsync();
        }

        var result = await apply.WaitAsync(CompletionTimeout);
        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.PersistedRecoveryRequired, result.ReasonCode);
        await AssertNoBusinessChangesAsync(account);

        var persisted = await ReadStateAsync(account);
        Assert.True(persisted.RecoveryRequired);
        Assert.Equal(recovery.RecoveryRequestEpoch, persisted.RecoveryRequestEpoch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_commit_before_recovery_preserves_the_later_recovery_request(bool wallet)
    {
        var (account, generation) = await CreateFullyReconciledAccountAsync();
        await using var observerContext = fixture.CreateContext();
        var observerPid = await GetBackendPidAsync(observerContext);
        var beforeCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new GateBeforeCommitTransaction(
            new ExchangeAccountSyncTransaction(observerContext),
            beforeCommit,
            allowCommit);
        var apply = ApplyStreamAsync(account, generation, wallet, observerContext, gate);
        await beforeCommit.Task.WaitAsync(CompletionTimeout);

        await using var recoveryContext = fixture.CreateContext();
        var recoveryPid = await GetBackendPidAsync(recoveryContext);
        await using var recoveryTransaction = await recoveryContext.Database.BeginTransactionAsync();
        var recoveryStore = new RealtimeObservationStateStore(recoveryContext);
        var current = await recoveryStore.GetAsync(account.UserId, account.Id);
        Assert.NotNull(current);
        var recovery = current!.Value with
        {
            RecoveryRequired = true,
            RecoveryRequestEpoch = current.Value.RecoveryRequestEpoch + 1,
        };
        var saveRecovery = recoveryStore.SaveAsync(account.UserId, recovery, current.Version);
        try
        {
            await WaitForPostgresLockWaitAsync(recoveryPid);
        }
        finally
        {
            allowCommit.TrySetResult(true);
        }

        var result = await apply.WaitAsync(CompletionTimeout);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        await saveRecovery.WaitAsync(CompletionTimeout);
        await recoveryTransaction.CommitAsync();

        var persisted = await ReadStateAsync(account);
        Assert.True(persisted.RecoveryRequired);
        Assert.Equal(recovery.RecoveryRequestEpoch, persisted.RecoveryRequestEpoch);
        Assert.Equal(T1, persisted.LastFullyReconciledAt);
    }

    [Fact]
    public async Task Full_rest_reconciliation_confirms_epoch_and_reenables_stream_observations()
    {
        var (account, generation) = await CreateObservedAccountAsync(recoveryRequired: true);
        var clock = new FixedTimeProvider(T1.AddMinutes(1));
        await using var services = CreateApplicationServices(account, new TestPrivateProvider(), clock);
        var coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var reconciliation = await coordinator.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(reconciliation.FullReconciliationCommitted);
        var confirmed = await ReadStateAsync(account);
        Assert.False(confirmed.RecoveryRequired);
        Assert.Equal(1, confirmed.RecoveryRequestEpoch);
        Assert.Equal(T1.AddMinutes(1), confirmed.LastFullyReconciledAt);

        var result = await ApplyAsync(account, generation, OpenObservation(account, T1.AddMinutes(2), 1));
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
    }

    [Fact]
    public async Task Scoped_rest_confirmation_does_not_clear_account_recovery_fence()
    {
        var (account, generation) = await CreateFullyReconciledAccountAsync();
        var recovery = await PersistNewRecoveryRequestAsync(account);
        var provider = new TestPrivateProvider { ObservedAt = T1.AddMinutes(2) };
        provider.SetPosition(OpenPosition());

        await using (var context = fixture.CreateContext())
        {
            var result = await CreateRealtimeService(
                    context,
                    provider,
                    clock: new FixedTimeProvider(T1.AddMinutes(3)))
                .ApplyAsync(
                    account.UserId,
                    generation,
                    OpenObservation(account, T1.AddMinutes(1), 1),
                    confirmWithRest: true);

            Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
            Assert.True(result.ConfirmedByRest);
        }

        var persisted = await ReadStateAsync(account);
        Assert.True(persisted.RecoveryRequired);
        Assert.Equal(recovery.RecoveryRequestEpoch, persisted.RecoveryRequestEpoch);

        var websocket = await ApplyAsync(account, generation, OpenObservation(account, T1.AddMinutes(2), 2));
        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, websocket.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.PersistedRecoveryRequired, websocket.ReasonCode);
    }

    private async Task<(ExchangeAccount Account, long Generation)> CreateFullyReconciledAccountAsync()
    {
        return await CreateObservedAccountAsync();
    }

    private async Task<RealtimeObservationState> PersistNewRecoveryRequestAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var store = new RealtimeObservationStateStore(context);
        var current = await store.GetAsync(account.UserId, account.Id);
        Assert.NotNull(current);
        var recovery = current!.Value with
        {
            RecoveryRequired = true,
            RecoveryRequestEpoch = current.Value.RecoveryRequestEpoch + 1,
        };
        await store.SaveAsync(account.UserId, recovery, current.Version);
        return recovery;
    }

    private Task<RealtimeObservationApplyResult> ApplyStreamAsync(
        ExchangeAccount account,
        long generation,
        bool wallet,
        TradeSystemDbContext context,
        IExchangeAccountSyncTransaction transaction) =>
        wallet
            ? CreateWalletService(context, transaction)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1.AddSeconds(1)))
            : CreateRealtimeService(context, new TestPrivateProvider(), transaction)
                .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(1), 1));

    private async Task AssertNoBusinessChangesAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(0, await context.PositionChanges.CountAsync(change =>
            context.Positions.Any(position =>
                position.Id == change.PositionId &&
                position.ExchangeAccountId == account.Id.Value)));
        var persistedAccount = await new ExchangeAccountRepository(context).GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(persistedAccount);
        Assert.Null(persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Null(await new WalletObservationCursorStore(context)
            .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
        Assert.Null(await new PortfolioStateRepository(context).GetLatestAsync(account.UserId, account.Id));
        Assert.Equal(0, await CountOutboxAsync(context, account));
    }

    private async Task<int> GetBackendPidAsync(TradeSystemDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task WaitForPostgresLockWaitAsync(int processId)
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT wait_event_type FROM pg_stat_activity WHERE pid = @pid";
        var pid = command.CreateParameter();
        pid.ParameterName = "pid";
        pid.Value = processId;
        command.Parameters.Add(pid);

        using var timeout = new CancellationTokenSource(CompletionTimeout);
        while (true)
        {
            var waitEvent = await command.ExecuteScalarAsync(timeout.Token);
            if (waitEvent is string eventType && eventType == "Lock")
            {
                return;
            }

            await Task.Yield();
        }
    }

    private sealed class SignalAfterAccountLockTransaction(
        IExchangeAccountSyncTransaction inner,
        TaskCompletionSource<bool> accountLockAcquired) : IExchangeAccountSyncTransaction
    {
        public Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.LockPositionsAsync(userId, exchangeAccountId, cancellationToken);

        public async Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            await inner.LockAccountAsync(userId, exchangeAccountId, cancellationToken);
            accountLockAcquired.TrySetResult(true);
        }

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(operation, cancellationToken);
    }

    private sealed class GateBeforeCommitTransaction(
        IExchangeAccountSyncTransaction inner,
        TaskCompletionSource<bool> beforeCommit,
        TaskCompletionSource<bool> allowCommit) : IExchangeAccountSyncTransaction
    {
        public Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.LockPositionsAsync(userId, exchangeAccountId, cancellationToken);

        public Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.LockAccountAsync(userId, exchangeAccountId, cancellationToken);

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(
                async token =>
                {
                    await operation(token);
                    beforeCommit.TrySetResult(true);
                    await allowCommit.Task.WaitAsync(CompletionTimeout, token);
                },
                cancellationToken);
    }
}
