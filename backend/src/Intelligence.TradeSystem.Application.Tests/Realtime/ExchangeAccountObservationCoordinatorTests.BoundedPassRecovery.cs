using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

/// <summary>
/// Запрос recovery, который ограниченный проход оставляет следующему вызову, сохраняется в persisted-состоянии
/// до штатного возврата: исчерпание бюджета наблюдений или recovery-попытки не оставляет его только в памяти.
/// </summary>
public sealed partial class ExchangeAccountObservationCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovery_Required_By_The_Last_Observation_Of_The_Budget_Is_Persisted(bool missingRevision)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var stateWrites = new List<RealtimeObservationState>();
        world.BeforeStateSave = stateWrites.Add;
        var requestsBefore = world.Provider.PositionRequests.Count;
        var observation = missingRevision
            ? world.OpenObservation(syncAt.AddSeconds(1), null) with { ProviderUpdatedAt = null }
            : AmbiguousObservation(world, syncAt.AddSeconds(1), 1);
        coordinator.TryEnqueue(world.UserId, 1, observation).Should().BeTrue();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(missingRevision
            ? RealtimeObservationApplyResult.MissingRevision
            : RealtimeObservationApplyResult.AmbiguousObservation);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.Generation.Should().Be(1);
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore, "the pass has no recovery budget left");
        stateWrites.Should().ContainSingle().Which.Should().Match<RealtimeObservationState>(
            state => state.RecoveryRequired && state.Generation == 1);
    }

    [Fact]
    public async Task Recovery_Required_At_The_Capacity_Boundary_Is_Persisted_And_Later_Observations_Are_Not_Applied()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 3);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var requestsBefore = world.Provider.PositionRequests.Count;
        bool? fourthEnqueued = null;
        world.BeforeTransactionExecuteAsync = _ =>
        {
            world.BeforeTransactionExecuteAsync = null;
            fourthEnqueued = coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(4), 4));
            return Task.CompletedTask;
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(1), 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(2), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(3), 3)).Should().BeTrue();

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        fourthEnqueued.Should().BeTrue();
        results.Select(result => result.Outcome).Should().Equal(
            RealtimeObservationApplyOutcome.Applied,
            RealtimeObservationApplyOutcome.Applied,
            RealtimeObservationApplyOutcome.RecoveryRequired);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore);

        world.Clock.UtcNow = syncAt.AddSeconds(10);
        var recovery = await coordinator
            .ProcessPendingAsync(world.UserId, world.AccountId)
            .WaitAsync(TimeSpan.FromSeconds(10));

        recovery.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2, "the recovery sync supersedes the buffered fourth observation");
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Recovery_Detected_Before_The_Budget_Is_Exhausted_Runs_One_Full_Sync_That_Clears_It()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 3);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var requestsBefore = world.Provider.PositionRequests.Count;
        var recoveryFlags = new List<bool>();
        world.Provider.OnPositionsRequest = () => recoveryFlags.Add(world.State!.RecoveryRequired);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(2), 2)).Should().BeTrue();
        world.Clock.UtcNow = syncAt.AddSeconds(10);

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservation);
        recoveryFlags.Should().Equal(true);
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        world.Cursor("BTCUSDT").Should().BeNull();
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Recovery_Required_After_The_Single_Recovery_Attempt_Of_A_Pass_Is_Persisted()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 3);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var requestsBefore = world.Provider.PositionRequests.Count;
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        world.Provider.OnPositionsRequest = () =>
        {
            world.Provider.OnPositionsRequest = null;
            coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(11), 2)).Should().BeTrue();
        };
        world.Clock.UtcNow = syncAt.AddSeconds(10);

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Select(result => result.ReasonCode).Should().Equal(
            RealtimeObservationApplyResult.AmbiguousObservation,
            RealtimeObservationApplyResult.AmbiguousObservation);
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeTrue("the second recovery request is left to the next pass");
        world.State.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));

        world.Clock.UtcNow = syncAt.AddSeconds(20);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null, null]);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(20));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task Failed_Recovery_Write_At_The_End_Of_A_Pass_Is_Not_Suppressed(int failures, bool persistedByRetry)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        world.StateSaveFailuresToInject = failures;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        thrown.Message.Should().Be("Injected observation state persistence failure.");
        world.State!.RecoveryRequired.Should().Be(persistedByRetry);
        await AssertLocalRecoveryIsRetriedAsync(world, coordinator, syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Lost_Cas_Retries_Within_The_Bound_Persist_Recovery_At_The_End_Of_A_Pass()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        world.StateConflictsToInject = 2;

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.StateConflictsToInject.Should().Be(0);
        world.State!.RecoveryRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Exhausted_Cas_Retries_At_The_End_Of_A_Pass_Surface_The_Conflict_And_Keep_The_Local_Request()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        var saveAttempts = 0;
        world.BeforeStateSave = _ => saveAttempts++;
        world.StateConflictsToInject = int.MaxValue;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId));

        saveAttempts.Should().Be(6, "the regular write and the exceptional-path write are each bounded by three attempts");
        world.State!.RecoveryRequired.Should().BeFalse();
        world.StateConflictsToInject = 0;
        world.BeforeStateSave = null;
        await AssertLocalRecoveryIsRetriedAsync(world, coordinator, syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Generation_Advanced_Before_The_Recovery_Write_Is_Not_Overwritten()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        var stateWrites = new List<RealtimeObservationState>();
        world.BeforeStateSave = stateWrites.Add;
        world.BeforeStateRead = () =>
        {
            world.BeforeStateRead = null;
            world.AdvanceGeneration();
            world.State!.RecoveryRequired.Should().BeTrue();
        };

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        stateWrites.Should().BeEmpty();
        world.State!.Generation.Should().Be(2);

        world.Clock.UtcNow = syncAt.AddSeconds(10);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        stateWrites.Should().ContainSingle().Which.Should().Match<RealtimeObservationState>(
            state => state.Generation == 2 && !state.RecoveryRequired);
        world.State!.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Generation_Advanced_Between_Read_And_Recovery_Write_Is_Not_Overwritten()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        var advancedState = default(RealtimeObservationState);
        world.BeforeStateSave = _ =>
        {
            world.BeforeStateSave = null;
            world.AdvanceGeneration();
            advancedState = world.State;
        };

        var results = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.State.Should().BeSameAs(advancedState, "the generation 1 write lost the CAS and was not retried into generation 2");
        world.State!.Generation.Should().Be(2);
    }

    [Fact]
    public async Task Cancellation_Of_The_Recovery_Write_At_The_End_Of_A_Pass_Still_Persists_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        world.BeforeStateRead = () =>
        {
            world.BeforeStateRead = null;
            cancellation.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId, cancellation.Token));

        world.State!.RecoveryRequired.Should().BeTrue("the exceptional-path write uses its own bounded token");
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
        await AssertLocalRecoveryIsRetriedAsync(world, coordinator, syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Cancellation_While_Applying_The_Last_Observation_Of_The_Budget_Persists_Recovery()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(1), 1)).Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        world.BeforeTransactionExecuteAsync = token =>
        {
            world.BeforeTransactionExecuteAsync = null;
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ProcessPendingAsync(world.UserId, world.AccountId, cancellation.Token));

        world.Positions.Should().BeEmpty();
        world.State!.RecoveryRequired.Should().BeTrue();
        await AssertLocalRecoveryIsRetriedAsync(world, coordinator, syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Persisted_Recovery_Is_Cleared_By_The_Next_Pass_And_Not_Repeated_By_The_Third()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var requestsBefore = world.Provider.PositionRequests.Count;
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();

        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore);

        world.Clock.UtcNow = syncAt.AddSeconds(10);
        var recovery = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        recovery.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));
        var stateWrites = new List<RealtimeObservationState>();
        world.BeforeStateSave = stateWrites.Add;

        world.Clock.UtcNow = syncAt.AddSeconds(20);
        var idle = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        idle.Should().BeEmpty();
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal([null]);
        stateWrites.Should().BeEmpty();
        world.State!.LastFullyReconciledAt.Should().Be(syncAt.AddSeconds(10));
    }

    [Fact]
    public async Task Unresolved_Recovery_Is_Not_Rewritten_When_It_Is_Already_Persisted()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        coordinator.TryEnqueue(world.UserId, 1, AmbiguousObservation(world, syncAt.AddSeconds(1), 1)).Should().BeTrue();
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        world.State!.RecoveryRequired.Should().BeTrue();
        var stateWrites = new List<RealtimeObservationState>();
        world.BeforeStateSave = stateWrites.Add;
        world.Provider.Status = OpenPositionsObservationStatus.Failed;

        world.Clock.UtcNow = syncAt.AddSeconds(10);
        await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        stateWrites.Should().BeEmpty();
        world.State!.RecoveryRequired.Should().BeTrue();
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
    }

    [Fact]
    public async Task Overflow_While_The_Last_Observation_Of_The_Budget_Is_Applied_Is_Persisted()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        var coordinator = CreateCoordinator(world, provider, bufferCapacity: 1);
        var syncAt = await ReconcileWithEmptyBufferAsync(world, coordinator);
        var requestsBefore = world.Provider.PositionRequests.Count;
        var transactionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransaction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.BeforeTransactionExecuteAsync = async cancellationToken =>
        {
            world.BeforeTransactionExecuteAsync = null;
            transactionStarted.SetResult();
            await releaseTransaction.Task.WaitAsync(cancellationToken);
        };
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(1), 1)).Should().BeTrue();
        var processing = coordinator.ProcessPendingAsync(world.UserId, world.AccountId);
        await transactionStarted.Task;

        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(2), 2)).Should().BeTrue();
        coordinator.TryEnqueue(world.UserId, 1, world.OpenObservation(syncAt.AddSeconds(3), 3)).Should().BeFalse();
        releaseTransaction.SetResult();
        var results = await processing;

        results.Should().ContainSingle().Which.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.State!.RecoveryRequired.Should().BeTrue();
        world.Provider.PositionRequests.Should().HaveCount(requestsBefore);

        world.Clock.UtcNow = syncAt.AddSeconds(10);
        var recovery = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        recovery.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.State!.RecoveryRequired.Should().BeFalse();
    }

    private static PrivatePositionObservation AmbiguousObservation(
        RealtimeTestWorld world,
        DateTimeOffset at,
        long sequence) =>
        world.OpenObservation(at, sequence) with { Kind = PrivatePositionObservationKind.Ambiguous };

    /// <summary>
    /// Делает поколение 1 известным координатору и снимает persisted-признак recovery стартовой полной
    /// сверкой без наблюдений в буфере.
    /// </summary>
    /// <returns>Момент сверки.</returns>
    private static async Task<DateTimeOffset> ReconcileWithEmptyBufferAsync(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator)
    {
        var syncAt = world.Clock.UtcNow;
        var sync = await coordinator.SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullReconciliationCommitted.Should().BeTrue();
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(syncAt);
        return syncAt;
    }

    /// <summary>
    /// Следующий проход видит оставшийся локальный запрос: сохраняет признак до REST-сверки и снимает его
    /// полной сверкой.
    /// </summary>
    private static async Task AssertLocalRecoveryIsRetriedAsync(
        RealtimeTestWorld world,
        ExchangeAccountObservationCoordinator coordinator,
        DateTimeOffset retryAt)
    {
        var recoveryFlags = new List<bool>();
        world.Provider.OnPositionsRequest = () => recoveryFlags.Add(world.State!.RecoveryRequired);
        world.Clock.UtcNow = retryAt;

        var recovery = await coordinator.ProcessPendingAsync(world.UserId, world.AccountId);

        world.Provider.OnPositionsRequest = null;
        recovery.Should().ContainSingle().Which.ReasonCode.Should().Be(RealtimeObservationApplyResult.BufferOverflow);
        recoveryFlags.Should().Equal(true);
        world.State!.RecoveryRequired.Should().BeFalse();
        world.State.LastFullyReconciledAt.Should().Be(retryAt);
    }
}
