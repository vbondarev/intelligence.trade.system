using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    private static readonly string[] CloseLockOrder = ["positions", "account"];
    private static readonly string[] RetriedObservationLockOrder =
        ["positions", "account", "positions", "account"];

    [Fact]
    public async Task Active_close_between_watermark_and_locks_retries_without_stale_reopen_or_partial_cursor_write()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var initialOpen = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1, sequence: 1));
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, initialOpen.Outcome);
        await using (var beforeRaceContext = fixture.CreateContext())
        {
            var current = Assert.Single(await new PositionRepository(beforeRaceContext)
                .GetCurrentWithHistoryAsync(account.UserId, account.Id));
            var watermark = Assert.Single(await new PositionRepository(beforeRaceContext)
                .GetCurrentVersionWatermarksAsync(account.UserId, account.Id));
            Assert.Equal(current.Value.Id, watermark.PositionId);
            Assert.Equal(current.Version, watermark.Version);
            Assert.Equal(PositionTrackingState.Active, current.Value.TrackingState);
        }

        var lockReached = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePositionLock = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleObservedAt = T1.AddSeconds(5);
        var closeObservedAt = T1.AddSeconds(10);
        await using var observationContext = fixture.CreateContext();
        var observationTransaction = new OrderedSyncTransaction(
            new ExchangeAccountSyncTransaction(observationContext),
            lockReached,
            releasePositionLock);
        var staleProvider = new TestPrivateProvider { ObservedAt = staleObservedAt };
        var staleObservation = OpenObservation(
            account,
            staleObservedAt,
            sequence: 2,
            OpenPosition(size: 2m));
        var observationService = CreateRealtimeService(
            observationContext,
            staleProvider,
            observationTransaction,
            new FixedTimeProvider(staleObservedAt.AddSeconds(2)));
        var observationTask = observationService.ApplyAsync(
            account.UserId,
            generation,
            staleObservation);

        await lockReached.Task.WaitAsync(CompletionTimeout);

        await using var closeContext = fixture.CreateContext();
        var closeTransaction = new OrderedSyncTransaction(new ExchangeAccountSyncTransaction(closeContext));
        var closeProvider = new TestPrivateProvider
        {
            ObservedAt = closeObservedAt,
            PositionsObservedAt = closeObservedAt,
        };
        var closeResult = await new ExchangeAccountSyncService(
                new ExchangeAccountRepository(closeContext),
                new TestCredentialStore(),
                new TestPrivateProviderFactory(closeProvider),
                new PositionRepository(closeContext),
                new PortfolioStateRepository(closeContext),
                closeTransaction,
                new ApplicationEventOutbox(closeContext),
                new ExchangeRestOrderingEvidenceStore(closeContext),
                new WalletObservationCursorStore(closeContext),
                new FixedTimeProvider(closeObservedAt.AddSeconds(1)))
            .SynchronizeAsync(account.UserId, account.Id)
            .WaitAsync(CompletionTimeout);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, closeResult.Outcome);
        Assert.True(closeResult.FullReconciliationCommitted);
        Assert.Equal(CloseLockOrder, closeTransaction.LockOrder);
        var stateAfterClose = await ReadPersistedStateAsync(account);
        AssertClosedState(stateAfterClose, closeObservedAt);

        releasePositionLock.TrySetResult(true);
        var staleResult = await observationTask.WaitAsync(CompletionTimeout);

        Assert.Equal(RealtimeObservationApplyOutcome.Stale, staleResult.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.OlderThanClosure, staleResult.ReasonCode);
        Assert.Equal(2, observationTransaction.ExecutionCount);
        Assert.Equal(RetriedObservationLockOrder, observationTransaction.LockOrder);
        var stateAfterRetry = await ReadPersistedStateAsync(account);
        AssertClosedState(stateAfterRetry, closeObservedAt);
        Assert.Equal(stateAfterClose.PositionId, stateAfterRetry.PositionId);
        Assert.Equal(stateAfterClose.PositionVersion, stateAfterRetry.PositionVersion);
        Assert.Equal(stateAfterClose.ChangeKinds, stateAfterRetry.ChangeKinds);
        Assert.Equal(stateAfterClose.CursorSequence, stateAfterRetry.CursorSequence);
        Assert.Equal(stateAfterClose.CursorPositionId, stateAfterRetry.CursorPositionId);
        Assert.Equal(stateAfterClose.CursorPositionVersion, stateAfterRetry.CursorPositionVersion);
        Assert.Equal(stateAfterClose.CursorVersion, stateAfterRetry.CursorVersion);
        Assert.Equal(stateAfterClose.LastAppliedPositionsObservationAt, stateAfterRetry.LastAppliedPositionsObservationAt);
        Assert.Equal(stateAfterClose.AccountVersion, stateAfterRetry.AccountVersion);
        Assert.Equal(stateAfterClose.CurrentPositionCount, stateAfterRetry.CurrentPositionCount);
        Assert.Equal(stateAfterClose.PortfolioCount, stateAfterRetry.PortfolioCount);
        Assert.Equal(stateAfterClose.PortfolioCalculatedAt, stateAfterRetry.PortfolioCalculatedAt);
        Assert.Equal(stateAfterClose.PortfolioPositionCount, stateAfterRetry.PortfolioPositionCount);
        Assert.Equal(stateAfterClose.OutboxCount, stateAfterRetry.OutboxCount);
        Assert.Equal(stateAfterClose.PositionClosedOutboxCount, stateAfterRetry.PositionClosedOutboxCount);
    }

    private async Task<ActiveCloseRaceState> ReadPersistedStateAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        var cursor = await new PositionObservationCursorStore(context)
            .GetAsync(account.UserId, CursorKey(account));
        Assert.NotNull(cursor);
        var latestPortfolio = await new PortfolioStateRepository(context)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latestPortfolio);
        var storedAccount = await new ExchangeAccountRepository(context)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(storedAccount);
        var currentPositions = await new PositionRepository(context)
            .GetCurrentVersionWatermarksAsync(account.UserId, account.Id);

        return new ActiveCloseRaceState(
            position.Value.Id,
            position.Value.TrackingState,
            position.Value.ClosedAt,
            position.Version.Value,
            position.Value.Changes.Select(change => change.Kind).ToArray(),
            cursor.Value.LastProviderSequence,
            cursor.Value.LastDirectAppliedPositionId,
            cursor.Value.LastDirectAppliedPositionVersion,
            cursor.Version.Value,
            storedAccount.Value.LastAppliedPositionsObservationAt,
            storedAccount.Version.Value,
            currentPositions.Count,
            await CountPortfoliosAsync(context, account),
            latestPortfolio.CalculatedAt,
            latestPortfolio.Positions.Count,
            await CountOutboxAsync(context, account),
            await CountOutboxAsync(context, account, ApplicationEventTypes.PositionClosed));
    }

    private static void AssertClosedState(ActiveCloseRaceState state, DateTimeOffset closedAt)
    {
        Assert.Equal(PositionTrackingState.Closed, state.TrackingState);
        Assert.Equal(closedAt, state.ClosedAt);
        Assert.Equal(2, state.PositionVersion);
        Assert.Equal(new[] { PositionChangeKind.New, PositionChangeKind.Closed }, state.ChangeKinds);
        Assert.Equal(1, state.CursorSequence);
        Assert.Equal(state.PositionId, state.CursorPositionId);
        Assert.Equal(ConcurrencyVersion.Initial, state.CursorPositionVersion);
        Assert.Equal(1, state.CursorVersion);
        Assert.Equal(closedAt, state.LastAppliedPositionsObservationAt);
        Assert.Equal(2, state.AccountVersion);
        Assert.Equal(0, state.CurrentPositionCount);
        Assert.Equal(2, state.PortfolioCount);
        Assert.Equal(1, state.PositionClosedOutboxCount);
    }

    private sealed class OrderedSyncTransaction(
        IExchangeAccountSyncTransaction inner,
        TaskCompletionSource<bool>? lockReached = null,
        TaskCompletionSource<bool>? releasePositionLock = null)
        : IExchangeAccountSyncTransaction
    {
        private readonly List<string> lockOrder = [];
        private int positionLockCount;
        private int executionCount;

        public int ExecutionCount => Volatile.Read(ref executionCount);

        public IReadOnlyList<string> LockOrder
        {
            get
            {
                lock (lockOrder)
                {
                    return lockOrder.ToArray();
                }
            }
        }

        public async Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            RecordLock("positions");
            if (Interlocked.Increment(ref positionLockCount) == 1 &&
                lockReached is not null &&
                releasePositionLock is not null)
            {
                lockReached.TrySetResult(true);
                await releasePositionLock.Task.WaitAsync(cancellationToken);
            }

            await inner.LockPositionsAsync(userId, exchangeAccountId, cancellationToken);
        }

        public Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            RecordLock("account");
            return inner.LockAccountAsync(userId, exchangeAccountId, cancellationToken);
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref executionCount);
            await inner.ExecuteAsync(operation, cancellationToken);
        }

        private void RecordLock(string name)
        {
            lock (lockOrder)
            {
                lockOrder.Add(name);
            }
        }
    }

    private sealed record ActiveCloseRaceState(
        PositionId PositionId,
        PositionTrackingState TrackingState,
        DateTimeOffset? ClosedAt,
        long PositionVersion,
        PositionChangeKind[] ChangeKinds,
        long? CursorSequence,
        PositionId? CursorPositionId,
        ConcurrencyVersion? CursorPositionVersion,
        long CursorVersion,
        DateTimeOffset? LastAppliedPositionsObservationAt,
        long AccountVersion,
        int CurrentPositionCount,
        int PortfolioCount,
        DateTimeOffset PortfolioCalculatedAt,
        int PortfolioPositionCount,
        int OutboxCount,
        int PositionClosedOutboxCount);
}
