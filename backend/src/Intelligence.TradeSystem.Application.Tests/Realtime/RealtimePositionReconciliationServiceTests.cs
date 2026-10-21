using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// R-208-02: scoped confirmation с равным временем и пустым Complete не закрывает позицию
    /// и не продвигает cursor (ReceivedAt отката часов ≤ LastObservedAt).
    /// </summary>
    [Fact]
    public async Task Equal_Timestamp_Empty_Complete_Scoped_Rest_Does_Not_Confirm_Close()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var eventsBefore = world.Events.Count;
        world.Provider.SnapshotObservedAt = T0;
        world.Clock.UtcNow = T0.AddSeconds(3);
        var close = world.CloseObservation(T0.AddSeconds(2), 2) with { ReceivedAt = T0 };

        var result = await service.ApplyAsync(world.UserId, 1, close);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(1m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.Events.Should().HaveCount(eventsBefore);
        world.Events.OfType<PositionClosedEventV1>().Should().BeEmpty();
    }

    /// <summary>
    /// R-208-02: scoped confirmation с равным временем и противоречащим размером не подтверждает закрытие.
    /// </summary>
    [Fact]
    public async Task Equal_Timestamp_Conflicting_Size_Scoped_Rest_Does_Not_Confirm_Close()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var eventsBefore = world.Events.Count;
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Provider.SnapshotObservedAt = T0;
        world.Clock.UtcNow = T0.AddSeconds(3);
        var close = world.CloseObservation(T0.AddSeconds(2), 2) with { ReceivedAt = T0 };

        var result = await service.ApplyAsync(world.UserId, 1, close);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Should().ContainSingle().Which.Size.Should().Be(1m);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.Events.Should().HaveCount(eventsBefore);
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
        world.Portfolios.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_Size_Requires_Recovery_When_Credentials_Of_An_Enabled_Account_Are_Unavailable(bool missing)
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var eventsBefore = world.Events.Count;
        world.CredentialsUnavailable = !missing;
        world.CredentialsMissing = missing;
        world.Clock.UtcNow = T0.AddSeconds(3);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationUnavailable);
        world.Provider.PositionRequests.Should().BeEmpty();
        world.ActivePositions.Should().ContainSingle().Which.TrackingState.Should().Be(PositionTrackingState.Active);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.Events.Should().HaveCount(eventsBefore);
        world.Portfolios.Should().ContainSingle();
    }

    [Fact]
    public async Task Rest_Snapshot_Older_Than_The_Confirmed_Close_Event_Does_Not_Close_Or_Advance_Cursor()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.SnapshotObservedAt = T0.AddSeconds(8);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(10), 20));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationStale);
        AssertUnchangedOpenLong(world, eventsBefore);
    }

    [Fact]
    public async Task Rest_Snapshot_Older_Than_The_Side_Change_Event_Does_Not_Flip_The_Position()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        var shortPosition = OpenPosition(side: PositionSide.Short);
        world.Provider.SetExchangePosition(shortPosition);
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.SnapshotObservedAt = T0.AddSeconds(8);

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(10), 20, shortPosition));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationStale);
        AssertUnchangedOpenLong(world, eventsBefore);
    }

    /// <summary>
    /// Снимок, запрошенный в момент формирования события по биржевым часам, но до его получения
    /// backend, не доказывает порядок; запрос, отправленный не раньше получения, — доказывает.
    /// </summary>
    [Theory]
    [InlineData(50, false)]
    [InlineData(0, true)]
    public async Task Rest_Snapshot_At_The_Boundary_Confirms_Only_When_Requested_After_Receipt(
        int snapshotLagMilliseconds,
        bool applied)
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        var close = world.CloseObservation(T0.AddSeconds(10), 20);
        world.Clock.UtcNow = close.ReceivedAt;
        world.Provider.SnapshotLag = TimeSpan.FromMilliseconds(snapshotLagMilliseconds);

        var result = await service.ApplyAsync(world.UserId, 1, close);

        if (applied)
        {
            result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
            result.ConfirmedByRest.Should().BeTrue();
            world.ActivePositions.Should().BeEmpty();
            world.Positions.Single().ClosedAt.Should().Be(close.ReceivedAt);
            world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(20);
        }
        else
        {
            result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
            result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationStale);
            AssertUnchangedOpenLong(world, eventsBefore);
        }
    }

    [Fact]
    public async Task Rest_Snapshot_Newer_Than_The_Event_Applies_The_Confirmed_State()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(12);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(10), 20));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().BeEmpty();
        world.Positions.Single().ClosedAt.Should().Be(T0.AddSeconds(12));
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(20);
        world.Events.OfType<PositionClosedEventV1>().Should().ContainSingle();
        world.Portfolios.Should().HaveCount(2);
        world.Portfolios[^1].Positions.Should().BeEmpty();
    }

    [Fact]
    public async Task Rest_Snapshot_Without_Observation_Time_Is_Not_Accepted()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.SnapshotObservedAt = default(DateTimeOffset);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(10), 20));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        AssertUnchangedOpenLong(world, eventsBefore);
    }

    [Fact]
    public async Task Generation_Change_During_Rest_Confirmation_Rejects_It_Without_Writes()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.OnPositionsRequest = world.AdvanceGeneration;

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(10), 20));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.GenerationSuperseded);
        AssertUnchangedOpenLong(world, eventsBefore);
    }

    [Fact]
    public async Task Event_Rejected_By_Stale_Rest_Snapshot_Is_Rechecked_With_A_Newer_Snapshot()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var close = world.CloseObservation(T0.AddSeconds(10), 20);
        world.Clock.UtcNow = T0.AddSeconds(12);
        world.Provider.SnapshotObservedAt = T0.AddSeconds(8);
        var rejected = await service.ApplyAsync(world.UserId, 1, close);
        world.Provider.SnapshotObservedAt = null;
        world.Clock.UtcNow = T0.AddSeconds(15);

        var retried = await service.ApplyAsync(world.UserId, 1, close);

        rejected.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        retried.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        retried.ConfirmedByRest.Should().BeTrue();
        world.ActivePositions.Should().BeEmpty();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(20);
        world.Events.OfType<PositionClosedEventV1>().Should().ContainSingle();
    }

    [Fact]
    public async Task Rest_Snapshot_Not_Newer_Than_The_Applied_Full_Sync_Does_Not_Confirm()
    {
        var world = await WorldWithOpenPositionAsync();
        var service = world.CreatePositionService();
        var eventsBefore = world.Events.Count;
        world.SetPositionsWatermark(T0.AddSeconds(13));
        world.Clock.UtcNow = T0.AddSeconds(12);

        var result = await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(10), 20));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationStale);
        AssertUnchangedOpenLong(world, eventsBefore);
    }

    /// <summary>Активная Long-позиция, принятая по наблюдению T0 с <c>seq = 10</c>.</summary>
    private static async Task<RealtimeTestWorld> WorldWithOpenPositionAsync()
    {
        var world = new RealtimeTestWorld();
        world.Clock.UtcNow = T0.AddSeconds(1);
        var opened = await world.CreatePositionService().ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 10));
        opened.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        return world;
    }

    private static void AssertUnchangedOpenLong(RealtimeTestWorld world, int eventsBefore)
    {
        var position = world.Positions.Should().ContainSingle().Subject;
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Long);
        position.LastObservedAt.Should().Be(T0);
        position.Changes.Should().ContainSingle();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(10);
        world.Events.Should().HaveCount(eventsBefore);
        world.Portfolios.Should().ContainSingle();
    }

    [Fact]
    public async Task Rest_Snapshot_Older_Than_The_Position_Does_Not_Close_It()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(11);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 1));
        // Позиция записана экземпляром с опережающими часами: снимок запрошен после получения
        // сообщения, но раньше последнего наблюдения позиции.
        world.Clock.UtcNow = T0.AddSeconds(6);
        var close = world.CloseObservation(T0.AddSeconds(11), 2) with { ReceivedAt = T0.AddSeconds(5) };

        var result = await service.ApplyAsync(world.UserId, 1, close);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.ActivePositions.Should().ContainSingle();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
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
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationStale);
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

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    public async Task Delayed_Open_Older_Than_Unsuccessful_Rest_Attempt_Is_Confirmed_With_Rest(
        OpenPositionsObservationStatus attemptStatus)
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        world.Clock.UtcNow = T0.AddSeconds(10);
        world.Provider.Status = attemptStatus;
        var sync = await provider.GetRequiredService<ExchangeAccountObservationCoordinator>()
            .SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullReconciliationCommitted.Should().BeFalse();
        world.Account.LastAppliedPositionsObservationAt.Should().Be(T0.AddSeconds(10));
        world.Positions.Should().BeEmpty();
        world.Provider.Status = OpenPositionsObservationStatus.Complete;
        world.Provider.SetExchangePosition(OpenPosition());
        world.Clock.UtcNow = T0.AddSeconds(11);
        var requestsBefore = world.Provider.PositionRequests.Count;

        var result = await world.CreatePositionService()
            .ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(9), 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal("BTCUSDT");
        var opened = world.ActivePositions.Should().ContainSingle().Subject;
        opened.LastObservedAt.Should().Be(T0.AddSeconds(11));
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(1);
        world.Portfolios[^1].Positions.Should().ContainSingle();
    }

    [Fact]
    public async Task Delayed_Open_Refuted_By_Fresh_Complete_Rest_Snapshot_Requires_Recovery_Without_Cursor()
    {
        var world = new RealtimeTestWorld();
        using var provider = world.CreateServiceProvider();
        world.Clock.UtcNow = T0.AddSeconds(10);
        var sync = await provider.GetRequiredService<ExchangeAccountObservationCoordinator>()
            .SynchronizeAsync(world.UserId, world.AccountId);
        sync.FullReconciliationCommitted.Should().BeTrue();
        world.Clock.UtcNow = T0.AddSeconds(11);
        var eventsBefore = world.Events.Count;
        var portfoliosBefore = world.Portfolios.Count;
        var requestsBefore = world.Provider.PositionRequests.Count;

        var result = await world.CreatePositionService()
            .ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(9), 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservationOrder);
        world.Positions.Should().BeEmpty();
        world.Events.OfType<PositionOpenedEventV1>().Should().BeEmpty();
        world.Events.Should().HaveCount(eventsBefore);
        world.Portfolios.Should().HaveCount(portfoliosBefore);
        world.Provider.PositionRequests.Skip(requestsBefore).Should().Equal("BTCUSDT");
        world.Cursor("BTCUSDT").Should().BeNull();
    }

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    public async Task Delayed_Open_Without_Available_Rest_Confirmation_Requires_Recovery_Without_Cursor_Advance(
        OpenPositionsObservationStatus confirmationStatus)
    {
        var world = new RealtimeTestWorld();
        world.SetPositionsWatermark(T0.AddSeconds(10));
        world.Provider.SetExchangePosition(OpenPosition());
        world.Provider.Status = confirmationStatus;
        world.Clock.UtcNow = T0.AddSeconds(11);

        var result = await world.CreatePositionService()
            .ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(9), 1));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.Positions.Should().BeEmpty();
        world.Portfolios.Should().BeEmpty();
        world.Events.Should().BeEmpty();
        world.Cursor("BTCUSDT").Should().BeNull();
    }

    [Fact]
    public async Task Sub_Microsecond_Observations_Ahead_Of_A_Rolled_Back_Clock_Keep_Snapshot_Order()
    {
        var world = new RealtimeTestWorld();
        var positionService = world.CreatePositionService();
        var walletService = world.CreateWalletService();
        world.Clock.UtcNow = T0.AddSeconds(1);

        var opened = await positionService.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(5).AddTicks(1), 1));
        var wallet = await walletService.ApplyAsync(
            world.UserId,
            1,
            world.WalletObservation(T0.AddSeconds(5).AddTicks(3)));
        var updated = await positionService.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(6).AddTicks(1), 2, OpenPosition(size: 2m)));

        opened.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        wallet.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        updated.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        world.Portfolios.Select(portfolio => portfolio.CalculatedAt).Should().Equal(
            T0.AddSeconds(5).AddTicks(TimeSpan.TicksPerMicrosecond),
            T0.AddSeconds(5).AddTicks(2 * TimeSpan.TicksPerMicrosecond),
            T0.AddSeconds(6).AddTicks(TimeSpan.TicksPerMicrosecond));
        var latest = world.Portfolios[^1];
        latest.Capital.ObservedAt.Should().Be(T0.AddSeconds(5).AddTicks(3));
        latest.Positions.Should().ContainSingle().Which.Size.Should().Be(2m);
        latest.CalculatedAt.Should().BeOnOrAfter(world.ActivePositions.Single().LastObservedAt);
    }

    [Fact]
    public async Task Reopen_After_Closure_But_Older_Than_Failed_Rest_Attempt_Is_Confirmed_With_Rest()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(2), 2));
        var closedId = world.Positions.Should().ContainSingle().Subject.Id;
        world.SetPositionsWatermark(T0.AddSeconds(20));
        world.Provider.SetExchangePosition(OpenPosition(size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(21);

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(15), 3, OpenPosition(size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        var reopened = world.ActivePositions.Should().ContainSingle().Subject;
        reopened.Id.Should().NotBe(closedId);
        reopened.Size.Should().Be(2m);
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
    public async Task Delayed_Update_Older_Than_The_Latest_History_Change_Requires_Recovery_Without_Writes()
    {
        var world = new RealtimeTestWorld();
        var position = world.SeedPosition("BTCUSDT", T0, T0, seeded => seeded.MarkUnknown(T0.AddSeconds(10)));
        world.Clock.UtcNow = T0.AddSeconds(12);

        var result = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(5), 5, OpenPosition(size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.OlderThanPositionHistory);
        var stored = world.Positions.Should().ContainSingle().Subject;
        stored.Id.Should().Be(position.Id);
        stored.TrackingState.Should().Be(PositionTrackingState.Unknown);
        stored.Size.Should().Be(1m);
        stored.LastObservedAt.Should().Be(T0);
        stored.Changes.Select(change => (change.Kind, change.OccurredAt)).Should().Equal(
            (PositionChangeKind.New, T0),
            (PositionChangeKind.MarkedUnknown, T0.AddSeconds(10)));
        world.Cursor("BTCUSDT").Should().BeNull();
        world.Portfolios.Should().BeEmpty();
        world.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Newer_Than_The_Latest_History_Change_Recovers_The_Position()
    {
        var world = new RealtimeTestWorld();
        world.SeedPosition("BTCUSDT", T0, T0, seeded => seeded.MarkUnknown(T0.AddSeconds(10)));
        world.Clock.UtcNow = T0.AddSeconds(12);

        var result = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(11), 11));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var stored = world.ActivePositions.Should().ContainSingle().Subject;
        stored.LastObservedAt.Should().Be(T0.AddSeconds(11));
        stored.Changes.Select(change => (change.Kind, change.OccurredAt)).Should().Equal(
            (PositionChangeKind.New, T0),
            (PositionChangeKind.MarkedUnknown, T0.AddSeconds(10)),
            (PositionChangeKind.Recovered, T0.AddSeconds(11)));
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(11);
        world.Events.OfType<PositionChangedEventV1>().Should().ContainSingle();
    }

    [Fact]
    public async Task Scoped_Rest_Snapshot_Older_Than_The_Latest_History_Change_Does_Not_Rewrite_History()
    {
        var world = new RealtimeTestWorld();
        var staleAt = T0.AddMinutes(10);
        world.SeedPosition("BTCUSDT", T0, T0, seeded => seeded.RefreshFreshness(staleAt, TimeSpan.FromMinutes(5)));
        // Часы backend откатились после пометки устаревания, сделанной сверкой другого символа.
        world.Clock.UtcNow = T0.AddMinutes(6);

        var result = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            1,
            world.CloseObservation(T0.AddMinutes(5), 11));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.RestConfirmationIncomplete);
        world.Provider.PositionRequests.Should().Equal("BTCUSDT");
        var stored = world.Positions.Should().ContainSingle().Subject;
        stored.TrackingState.Should().Be(PositionTrackingState.Stale);
        stored.Changes.Select(change => (change.Kind, change.OccurredAt)).Should().Equal(
            (PositionChangeKind.New, T0),
            (PositionChangeKind.MarkedStale, staleAt));
        world.Cursor("BTCUSDT").Should().BeNull();
        world.Portfolios.Should().BeEmpty();
        world.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Closed_Lifecycles_Observed_After_A_Clock_Rollback_Do_Not_Break_Scoped_Confirmation()
    {
        var world = new RealtimeTestWorld();
        var closedBtc = new[]
        {
            world.SeedPosition("BTCUSDT", T0.AddSeconds(8), T0.AddSeconds(8), seeded => seeded.Close(T0.AddSeconds(8))),
            world.SeedPosition("BTCUSDT", T0.AddSeconds(9), T0.AddSeconds(10), seeded => seeded.Close(T0.AddSeconds(10))),
        };
        var closedSol = world.SeedPosition(
            "SOLUSDT",
            T0.AddSeconds(12),
            T0.AddSeconds(12),
            seeded => seeded.Close(T0.AddSeconds(12)));
        world.SeedPosition("ETHUSDT", T0.AddSeconds(1), T0.AddSeconds(1));
        world.Provider.SetExchangePosition(OpenPosition("ETHUSDT", size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(6);

        var result = await world.CreatePositionService().ApplyAsync(
            world.UserId,
            1,
            world.CloseObservation(T0.AddSeconds(2), 1, "ETHUSDT"));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        result.ConfirmedByRest.Should().BeTrue();
        var eth = world.ActivePositions.Should().ContainSingle().Subject;
        eth.ExchangePositionKey.InstrumentId.Value.Should().Be("ETHUSDT");
        eth.Size.Should().Be(2m);
        eth.LastObservedAt.Should().Be(T0.AddSeconds(6));
        foreach (var closed in closedBtc.Append(closedSol))
        {
            var stored = world.Positions.Single(position => position.Id == closed.Id);
            stored.TrackingState.Should().Be(PositionTrackingState.Closed);
            stored.LastObservedAt.Should().Be(closed.LastObservedAt);
            stored.ClosedAt.Should().Be(closed.ClosedAt);
            stored.Changes.Select(change => (change.Kind, change.OccurredAt))
                .Should().Equal(closed.Changes.Select(change => (change.Kind, change.OccurredAt)));
        }

        world.Cursor("ETHUSDT")!.LastProviderSequence.Should().Be(1);
        var latest = world.LatestPortfolio!;
        latest.Positions.Should().ContainSingle().Which.Size.Should().Be(2m);
        latest.CalculatedAt.Should().BeOnOrAfter(T0.AddSeconds(6));
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

    /// <summary>S-10: более новая ревизия при откате ReceivedAt не теряется как обычный Stale.</summary>
    [Fact]
    public async Task Newer_Revision_With_Rolled_Back_ReceivedAt_Requires_Recovery_Without_Mutating_State()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0.AddSeconds(10), 1));
        var positionBefore = world.ActivePositions.Single();
        var eventsBefore = world.Events.Count;
        var portfoliosBefore = world.Portfolios.Count;
        var cursorBefore = world.Cursor("BTCUSDT")!;

        var rolledBack = world.OpenObservation(T0.AddSeconds(11), 2, OpenPosition(size: 2m)) with
        {
            ReceivedAt = T0.AddSeconds(5),
        };
        var result = await service.ApplyAsync(world.UserId, 1, rolledBack);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservationOrder);
        var position = world.ActivePositions.Should().ContainSingle().Subject;
        position.Id.Should().Be(positionBefore.Id);
        position.Size.Should().Be(1m);
        position.LastObservedAt.Should().Be(T0.AddSeconds(10));
        position.Changes.Should().ContainSingle();
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(cursorBefore.LastProviderSequence);
        world.Portfolios.Should().HaveCount(portfoliosBefore);
        world.Events.Should().HaveCount(eventsBefore);
    }

    /// <summary>
    /// R-208-05 / G3: scoped REST Short старше закрытия Long того же слота не создаёт lifecycle.
    /// </summary>
    [Fact]
    public async Task Scoped_Rest_Opposite_Side_Older_Than_Closure_Requires_Recovery_Without_Open()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(9), 2));
        world.ActivePositions.Should().BeEmpty();
        var closedId = world.Positions.Should().ContainSingle().Subject.Id;
        // Watermark < REST.ObservedAt, но >= ObservedAt сообщения → ConfirmWithRest без stale watermark.
        world.SetPositionsWatermark(T0.AddSeconds(5));
        world.Provider.SnapshotObservedAt = T0.AddSeconds(6);
        world.Provider.SetExchangePosition(OpenPosition(side: PositionSide.Short, size: 2m));
        world.Clock.UtcNow = T0.AddSeconds(6);
        var eventsBefore = world.Events.Count;

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(5), 3, OpenPosition(side: PositionSide.Short, size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        world.ActivePositions.Should().BeEmpty();
        world.Positions.Should().ContainSingle().Which.Id.Should().Be(closedId);
        world.Events.OfType<PositionOpenedEventV1>().Should().HaveCount(1);
        world.Events.Count.Should().Be(eventsBefore);
    }

    /// <summary>R-208-05 / G5: Short строго новее закрытия Long на том же слоте создаёт новый lifecycle.</summary>
    [Fact]
    public async Task Opposite_Side_Newer_Than_Closure_Opens_Lifecycle()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var closedId = world.ActivePositions.Single().Id;
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(9), 2));
        world.Provider.SnapshotObservedAt = null;
        world.Clock.UtcNow = T0.AddSeconds(12);

        var result = await service.ApplyAsync(
            world.UserId,
            1,
            world.OpenObservation(T0.AddSeconds(11), 3, OpenPosition(side: PositionSide.Short, size: 2m)));

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.Applied);
        var opened = world.ActivePositions.Should().ContainSingle().Subject;
        opened.Id.Should().NotBe(closedId);
        opened.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Short);
        opened.LastObservedAt.Should().Be(T0.AddSeconds(11));
    }

    /// <summary>S-11: повторное открытие после закрытия при откате ReceivedAt требует recovery.</summary>
    [Fact]
    public async Task Reopen_After_Close_With_Rolled_Back_ReceivedAt_Requires_Recovery()
    {
        var world = new RealtimeTestWorld();
        var service = world.CreatePositionService();
        world.Clock.UtcNow = T0.AddSeconds(1);
        await service.ApplyAsync(world.UserId, 1, world.OpenObservation(T0, 1));
        var closedId = world.ActivePositions.Single().Id;
        // ClosedAt берётся из ObservedAt REST-снимка (T10); часы backend при подтверждении — T10.
        world.Provider.SnapshotObservedAt = T0.AddSeconds(10);
        world.Clock.UtcNow = T0.AddSeconds(10);
        await service.ApplyAsync(world.UserId, 1, world.CloseObservation(T0.AddSeconds(9), 2));
        world.ActivePositions.Should().BeEmpty();
        var closed = world.Positions.Single(position => position.Id == closedId);
        closed.ClosedAt.Should().Be(T0.AddSeconds(10));
        var eventsBefore = world.Events.Count;
        var portfoliosBefore = world.Portfolios.Count;

        // Биржевое время открытия новее ClosedAt, но ReceivedAt откатился раньше закрытия.
        var reopen = world.OpenObservation(T0.AddSeconds(11), 3) with { ReceivedAt = T0.AddSeconds(5) };
        var result = await service.ApplyAsync(world.UserId, 1, reopen);

        result.Outcome.Should().Be(RealtimeObservationApplyOutcome.RecoveryRequired);
        result.ReasonCode.Should().Be(RealtimeObservationApplyResult.AmbiguousObservationOrder);
        world.ActivePositions.Should().BeEmpty();
        var unchanged = world.Positions.Single(position => position.Id == closedId);
        unchanged.TrackingState.Should().Be(PositionTrackingState.Closed);
        unchanged.ClosedAt.Should().Be(closed.ClosedAt);
        unchanged.Changes.Should().HaveCount(closed.Changes.Count);
        world.Cursor("BTCUSDT")!.LastProviderSequence.Should().Be(2);
        world.Portfolios.Should().HaveCount(portfoliosBefore);
        world.Events.Should().HaveCount(eventsBefore);
    }
}
