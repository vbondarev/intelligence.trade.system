using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using static Intelligence.TradeSystem.Application.Tests.Realtime.RealtimeTestWorld;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimePositionReconciliationServiceTests
{
    [Fact]
    public async Task Open_Creates_Position_Cursor_Portfolio_And_Events_Without_Evaluation()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, sequence: 10));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeFalse();
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.Size.Should().Be(1m);
        position.LastObservedAt.Should().Be(T0);
        position.SettlementAsset.Should().Be(Usdt);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(10);
        world.Portfolios.Should().ContainSingle().Which.Positions.Should().ContainSingle();
        world.Events.Select(applicationEvent => applicationEvent.GetType())
            .Should()
            .Equal(typeof(PositionOpenedEventV1), typeof(PortfolioUpdatedEventV1));
        world.Provider.PositionRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Newer_Revision_Updates_The_Same_Lifecycle()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 10));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(1), 11, OpenPosition(size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.Size.Should().Be(2m);
        position.Changes.Select(change => change.Kind)
            .Should()
            .Equal(PositionChangeKind.New, PositionChangeKind.Increased);
        world.Events.OfType<PositionChangedEventV1>().Should().ContainSingle();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(11);
    }

    [Fact]
    public async Task Market_Only_Change_Does_Not_Create_Position_History()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 10));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(1), 11, OpenPosition(markPrice: 120m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.MarkPrice.Should().Be(120m);
        position.Changes.Should().ContainSingle();
        world.Events.OfType<PositionChangedEventV1>().Should().BeEmpty();
    }

    [Fact]
    public async Task Repeated_Revision_Is_Duplicate_Without_New_Events()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        var observation = world.OpenObservation(T0, 10);
        await service.ApplyAsync(world.UserId, 1, observation);
        var eventsBefore = world.Events.Count;

        var result = await service.ApplyAsync(world.UserId, 1, observation with { ReceivedAt = T0.AddSeconds(5) });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Duplicate);
        world.Events.Should().HaveCount(eventsBefore);
        world.Portfolios.Should().ContainSingle();
        world.ActivePositions.Single().Changes.Should().ContainSingle();
    }

    [Fact]
    public async Task Same_Revision_With_Different_Content_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 10));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, 10, OpenPosition(stopLoss: 90m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RevisionConflict);
        world.ActivePositions.Single().StopLoss.Should().BeNull();
    }

    [Fact]
    public async Task Older_Revision_Is_Stale()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(1), 11));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, 10, OpenPosition(size: 5m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderRevision);
        world.ActivePositions.Single().Size.Should().Be(1m);
    }

    [Fact]
    public async Task Contradicting_Revision_Components_Require_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 12));

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(1), 11, OpenPosition(size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RevisionConflict);
        world.ActivePositions.Single().Size.Should().Be(1m);
    }

    [Fact]
    public async Task Different_Symbols_Are_Ordered_By_Independent_Cursors()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();

        var eth = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(1), 50, OpenPosition("ETHUSDT")));
        var btc = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, 3, OpenPosition("BTCUSDT")));

        eth.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        btc.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.ActivePositions.Select(position => position.ExchangePositionKey.InstrumentId.Value)
            .Should()
            .BeEquivalentTo("ETHUSDT", "BTCUSDT");
        world.Cursor("ETHUSDT")!.LastProviderSequence.Should().Be(50);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(3);
    }

    [Fact]
    public async Task Zero_Size_Does_Not_Close_When_Rest_Still_Observes_The_Position()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(3);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.Provider.PositionRequests.Should().Equal("BTCUSDT");
        world.ActivePositions.Should().ContainSingle();
        world.Events.OfType<PositionClosedEventV1>().Should().BeEmpty();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
    }

    [Fact]
    public async Task Zero_Size_Closes_Only_After_Complete_Scoped_Rest_Snapshot()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        world.Clock.UtcNow = T0.AddSeconds(3);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().BeEmpty();
        var closed = world.Positions.Should().ContainSingle().Subject;
        closed.TrackingState.Should().Be(PositionTrackingState.Closed);
        closed.ClosedAt.Should().Be(T0.AddSeconds(3));
        world.Events.OfType<PositionClosedEventV1>().Should().ContainSingle();
    }

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    public async Task Zero_Size_Keeps_Last_Known_State_When_Rest_Snapshot_Is_Not_Complete(
        OpenPositionsObservationStatus status)
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var eventsBefore = world.Events.Count;
        world.Provider.Status = status;
        world.Clock.UtcNow = T0.AddSeconds(3);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Should().ContainSingle().Which.TrackingState.Should().Be(PositionTrackingState.Active);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.Events.Should().HaveCount(eventsBefore);
    }

    [Fact]
    public async Task Rest_Snapshot_Older_Than_The_Position_Does_Not_Close_It()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(11);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 1));
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.SnapshotLag = TimeSpan.FromSeconds(7);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(11), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Should().ContainSingle();
    }

    [Fact]
    public async Task Backend_Clock_Behind_The_Last_Position_Observation_Does_Not_Break_Rest_Confirmation()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(11);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 1));
        world.Clock.UtcNow = T0.AddSeconds(5);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(11), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.ActivePositions.Should().ContainSingle();
    }

    [Fact]
    public async Task Zero_Size_Without_Active_Lifecycle_Only_Advances_The_Cursor()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0, 7));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Positions.Should().BeEmpty();
        world.Events.Should().BeEmpty();
        world.Provider.PositionRequests.Should().BeEmpty();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(7);
    }

    [Fact]
    public async Task Reopen_After_Confirmed_Close_Creates_A_New_Position_Id()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var firstId = world.ActivePositions.Single().Id;
        world.Clock.UtcNow = T0.AddSeconds(3);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));
        world.Clock.UtcNow = T0.AddSeconds(6);

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(5), 3));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var reopened = world.ActivePositions.Should().ContainSingle().Subject;
        reopened.Id.Should().NotBe(firstId);
        world.Positions.Should().HaveCount(2);
        world.Positions.Single(position => position.Id == firstId).TrackingState
            .Should()
            .Be(PositionTrackingState.Closed);
    }

    [Fact]
    public async Task Open_Not_Newer_Than_Closure_Does_Not_Reopen()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(5), 3));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanClosure);
        world.ActivePositions.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_Not_Newer_Than_Full_Rest_Snapshot_Does_Not_Create_Position()
    {
        var world = new RealtimeTestWorld();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(5), 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Stale);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanFullSync);
        world.Positions.Should().BeEmpty();
        world.Cursor("BTCUSDT").Should().BeNull();
    }

    [Fact]
    public async Task One_Way_Side_Change_Is_Confirmed_With_Rest()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var shortPosition = OpenPosition(side: PositionSide.Short);
        world.Provider.SetExchangePosition(shortPosition);
        world.Clock.UtcNow = T0.AddSeconds(3);

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(2), 2, shortPosition));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Short);
        world.Events.OfType<PositionClosedEventV1>().Should().ContainSingle();
        world.Events.OfType<PositionOpenedEventV1>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Superseded_Generation_Rejects_The_Observation()
    {
        var world = new RealtimeTestWorld();
        world.AdvanceGeneration();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.GenerationSuperseded);
        world.Positions.Should().BeEmpty();
        world.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_Account_Is_Unavailable()
    {
        var world = new RealtimeTestWorld();
        world.DisableAccount();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.AccountUnavailable);
        world.Positions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PrivatePositionObservationKind.Duplicate, RealtimeObservationApplyOutcome.Duplicate)]
    [InlineData(PrivatePositionObservationKind.Unsupported, RealtimeObservationApplyOutcome.Ignored)]
    [InlineData(PrivatePositionObservationKind.Ambiguous, RealtimeObservationApplyOutcome.RecoveryRequired)]
    public async Task Classified_Observations_Do_Not_Change_State(
        PrivatePositionObservationKind kind,
        RealtimeObservationApplyOutcome expected)
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, 1) with { Kind = kind });

        result.Outcome.Should().Be(expected);
        world.TransactionAttempts.Should().Be(0);
        world.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task Observation_Without_Revision_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0, sequence: null) with { ProviderUpdatedAt = null });

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.MissingRevision);
        world.Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task Cursor_Conflict_Is_Retried_Without_Duplicate_History()
    {
        var world = new RealtimeTestWorld { CursorConflictsToInject = 1 };
        var service = world.CreatePositionService();

        var result = await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.TransactionAttempts.Should().Be(2);
        world.Positions.Should().ContainSingle().Which.Changes.Should().ContainSingle();
        world.Portfolios.Should().ContainSingle();
        world.Events.OfType<PositionOpenedEventV1>().Should().ContainSingle();
    }

    [Fact]
    public async Task Persistent_Conflicts_Exhaust_Bounded_Retries_Atomically()
    {
        var world = new RealtimeTestWorld { CursorConflictsToInject = 10 };
        var service = world.CreatePositionService();

        var act = () => service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        world.TransactionAttempts.Should().Be(3);
        world.Positions.Should().BeEmpty();
        world.Portfolios.Should().BeEmpty();
        world.Events.Should().BeEmpty();
    }
}
