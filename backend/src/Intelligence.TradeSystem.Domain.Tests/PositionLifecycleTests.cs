using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class PositionLifecycleTests
{
    private static readonly ExchangePositionKey Key = ExchangePositionKey.Create(
        ExchangeAccountId.New(), InstrumentId.From("BTCUSDT"), PositionSide.Long, 1);
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Position CreatePosition(decimal size = 1m, DateTimeOffset? at = null)
    {
        var t = at ?? T0;
        return Position.Create(Key, MarketCategory.Linear, size, t, t, averageEntryPrice: 100m, leverage: 2m);
    }

    [Fact]
    public void Create_Starts_Active_With_A_Single_New_Change()
    {
        var position = CreatePosition();

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.Changes.Should().ContainSingle();
        position.Changes[0].Kind.Should().Be(PositionChangeKind.New);
        position.Changes[0].Cause.Should().Be(PositionChangeCause.InitialObservation);
        position.Changes[0].Before.Should().BeNull();
        position.Changes[0].After.Size.Should().Be(1m);
    }

    [Fact]
    public void ApplyObservation_With_Larger_Size_Records_Increased()
    {
        var position = CreatePosition(size: 1m);

        var change = position.ApplyObservation(2m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m);

        change.Should().NotBeNull();
        change!.Kind.Should().Be(PositionChangeKind.Increased);
        position.Size.Should().Be(2m);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void ApplyObservation_With_Smaller_Positive_Size_Records_Reduced()
    {
        var position = CreatePosition(size: 3m);

        var change = position.ApplyObservation(1m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m);

        change!.Kind.Should().Be(PositionChangeKind.Reduced);
        position.Size.Should().Be(1m);
    }

    [Fact]
    public void ApplyObservation_Rejects_Zero_Size()
    {
        var position = CreatePosition();

        var act = () => position.ApplyObservation(0m, T0.AddMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ApplyObservation_With_Changed_Material_Field_Records_Updated()
    {
        var position = CreatePosition(size: 1m);

        var change = position.ApplyObservation(1m, T0.AddMinutes(1), averageEntryPrice: 110m, leverage: 2m);

        change!.Kind.Should().Be(PositionChangeKind.Updated);
        position.AverageEntryPrice.Should().Be(110m);
    }

    [Fact]
    public void ApplyObservation_With_Only_Dynamic_Field_Changes_Does_Not_Record_Updated()
    {
        var position = CreatePosition(size: 1m);

        var change = position.ApplyObservation(
            1m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m, markPrice: 999m, unrealizedPnl: 555m);

        change.Should().BeNull();
        position.Changes.Should().ContainSingle();
        position.MarkPrice.Should().Be(999m);
        position.UnrealizedPnl.Should().Be(555m);
    }

    [Fact]
    public void ApplyObservation_With_Only_PositionValue_Change_Does_Not_Record_Updated()
    {
        var position = CreatePosition(size: 1m);

        var change = position.ApplyObservation(
            1m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m, positionValue: 12345m);

        change.Should().BeNull();
        position.PositionValue.Should().Be(12345m);
    }

    [Fact]
    public void Repeated_Identical_Observation_Is_Idempotent()
    {
        var position = CreatePosition(size: 1m);

        var first = position.ApplyObservation(1m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m);
        var second = position.ApplyObservation(1m, T0.AddMinutes(1), averageEntryPrice: 100m, leverage: 2m);

        first.Should().BeNull();
        second.Should().BeNull();
        position.Changes.Should().ContainSingle(); // only the initial New
    }

    [Fact]
    public void ApplyObservation_Rejects_Observation_Older_Than_LastObservedAt()
    {
        var position = CreatePosition(at: T0.AddMinutes(5));

        var act = () => position.ApplyObservation(1m, T0.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkUnknown_Transitions_From_Active_And_Preserves_LastObservedAt()
    {
        var position = CreatePosition();
        var lastObservedAt = position.LastObservedAt;

        var change = position.MarkUnknown(T0.AddHours(1));

        change.Should().NotBeNull();
        change!.Kind.Should().Be(PositionChangeKind.MarkedUnknown);
        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
        position.LastObservedAt.Should().Be(lastObservedAt);
    }

    [Fact]
    public void MarkUnknown_Is_Idempotent_When_Already_Unknown()
    {
        var position = CreatePosition();
        position.MarkUnknown(T0.AddHours(1));

        var second = position.MarkUnknown(T0.AddHours(2));

        second.Should().BeNull();
        position.Changes.Should().HaveCount(2); // New + one MarkedUnknown
    }

    [Fact]
    public void MarkUnknown_Rejects_Observation_Older_Than_LastObservedAt()
    {
        var position = CreatePosition(at: T0.AddMinutes(5));

        var act = () => position.MarkUnknown(T0.AddMinutes(1), PositionChangeCause.PartialObservation);

        act.Should().Throw<InvalidOperationException>();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Failed_Observation_Marks_Unknown_Not_Closed()
    {
        var position = CreatePosition();

        position.MarkUnknown(T0.AddMinutes(1), PositionChangeCause.PositionsObservationFailed);

        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
        position.TrackingState.Should().NotBe(PositionTrackingState.Closed);
    }

    [Fact]
    public void ApplyObservation_After_Unknown_Recovers_To_Active()
    {
        var position = CreatePosition();
        position.MarkUnknown(T0.AddMinutes(1));

        var change = position.ApplyObservation(1m, T0.AddMinutes(2), averageEntryPrice: 100m, leverage: 2m);

        change.Should().NotBeNull();
        change!.Kind.Should().Be(PositionChangeKind.Recovered);
        change.Cause.Should().Be(PositionChangeCause.ObservationRestored);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void ApplyObservation_After_Stale_Recovers_To_Active()
    {
        var position = CreatePosition();
        position.RefreshFreshness(T0.AddHours(2), TimeSpan.FromHours(1));
        position.TrackingState.Should().Be(PositionTrackingState.Stale);

        var change = position.ApplyObservation(1m, T0.AddHours(3), averageEntryPrice: 100m, leverage: 2m);

        change!.Kind.Should().Be(PositionChangeKind.Recovered);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Recovery_With_Size_Increase_Records_Increased_With_ObservationRestored_Cause()
    {
        var position = CreatePosition();
        position.MarkUnknown(T0.AddMinutes(1));

        var change = position.ApplyObservation(2m, T0.AddMinutes(2), averageEntryPrice: 100m, leverage: 2m);

        change!.Kind.Should().Be(PositionChangeKind.Increased);
        change.Cause.Should().Be(PositionChangeCause.ObservationRestored);
        change.TrackingStateAfter.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Recovery_With_Material_Update_Records_Updated_With_ObservationRestored_Cause()
    {
        var position = CreatePosition();
        position.RefreshFreshness(T0.AddHours(2), TimeSpan.FromHours(1));

        var change = position.ApplyObservation(1m, T0.AddHours(3), averageEntryPrice: 110m, leverage: 2m);

        change!.Kind.Should().Be(PositionChangeKind.Updated);
        change.Cause.Should().Be(PositionChangeCause.ObservationRestored);
        change.TrackingStateAfter.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void RefreshFreshness_Marks_Stale_When_Older_Than_Threshold()
    {
        var position = CreatePosition();

        var change = position.RefreshFreshness(T0.AddHours(1).AddSeconds(1), TimeSpan.FromHours(1));

        change.Should().NotBeNull();
        change!.Kind.Should().Be(PositionChangeKind.MarkedStale);
        position.TrackingState.Should().Be(PositionTrackingState.Stale);
    }

    [Fact]
    public void RefreshFreshness_Does_Not_Mark_Stale_Exactly_At_Threshold()
    {
        var position = CreatePosition();

        var change = position.RefreshFreshness(T0.AddHours(1), TimeSpan.FromHours(1));

        change.Should().BeNull();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void RefreshFreshness_Rejects_Negative_StaleAfter()
    {
        var position = CreatePosition();

        var act = () => position.RefreshFreshness(T0, TimeSpan.FromTicks(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RefreshFreshness_Rejects_Now_Before_LastObservedAt()
    {
        var position = CreatePosition(at: T0.AddMinutes(1));

        var act = () => position.RefreshFreshness(T0, TimeSpan.Zero);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RefreshFreshness_Does_Not_Affect_Closed_Position()
    {
        var position = CreatePosition();
        position.Close(T0.AddMinutes(1));

        var change = position.RefreshFreshness(T0.AddYears(1), TimeSpan.FromHours(1));

        change.Should().BeNull();
        position.TrackingState.Should().Be(PositionTrackingState.Closed);
    }

    [Fact]
    public void Close_Transitions_To_Closed_With_MissingFromCompleteObservation_Cause()
    {
        var position = CreatePosition();

        var change = position.Close(T0.AddMinutes(1));

        change.Should().NotBeNull();
        change!.Kind.Should().Be(PositionChangeKind.Closed);
        change.Cause.Should().Be(PositionChangeCause.MissingFromCompleteObservation);
        position.TrackingState.Should().Be(PositionTrackingState.Closed);
    }

    [Fact]
    public void Close_Is_Idempotent()
    {
        var position = CreatePosition();
        position.Close(T0.AddMinutes(1));

        var second = position.Close(T0.AddMinutes(2));

        second.Should().BeNull();
        position.Changes.Should().HaveCount(2); // New + one Closed
        position.ClosedAt.Should().Be(T0.AddMinutes(1));
    }

    [Fact]
    public void Close_Rejects_Observation_Older_Than_LastObservedAt()
    {
        var position = CreatePosition(at: T0.AddMinutes(5));

        var act = () => position.Close(T0.AddMinutes(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ApplyObservation_Throws_When_Position_Already_Closed()
    {
        var position = CreatePosition();
        position.Close(T0.AddMinutes(1));

        var act = () => position.ApplyObservation(1m, T0.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Changes_Property_Is_Read_Only_And_Cannot_Be_Assigned()
    {
        var property = typeof(Position).GetProperty(nameof(Position.Changes))!;

        property.PropertyType.Should().Be<IReadOnlyList<PositionChange>>();
        property.SetMethod.Should().BeNull();
    }

    [Fact]
    public void Changes_Cannot_Be_Cast_To_A_Mutable_List()
    {
        var position = CreatePosition();

        var mutableChanges = position.Changes as List<PositionChange>;

        mutableChanges.Should().BeNull();
        position.Changes.Should().ContainSingle();
    }

    [Fact]
    public void History_Snapshot_Preserves_Size_AverageEntry_MarkPrice_Pnl_And_Liquidation()
    {
        var position = Position.Create(
            Key, MarketCategory.Linear, 2m, T0, T0,
            averageEntryPrice: 100m, markPrice: 105m, liquidationPrice: 80m, unrealizedPnl: 10m);

        var snapshot = position.Changes[0].After;

        snapshot.Size.Should().Be(2m);
        snapshot.AverageEntryPrice.Should().Be(100m);
        snapshot.MarkPrice.Should().Be(105m);
        snapshot.LiquidationPrice.Should().Be(80m);
        snapshot.UnrealizedPnl.Should().Be(10m);
    }
}
