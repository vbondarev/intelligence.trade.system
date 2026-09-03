using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PositionReconcilerTests
{
    private static readonly ExchangeAccountId AccountA = ExchangeAccountId.New();
    private static readonly ExchangeAccountId AccountB = ExchangeAccountId.New();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static OpenPosition CreateOpenPosition(
        string symbol = "BTCUSDT",
        MarketCategory category = MarketCategory.Linear,
        PositionSide side = PositionSide.Long,
        decimal size = 1m,
        int positionIdx = 0) =>
        new(symbol, category, side, PositionStatus.Normal, size,
            AvgPrice: 100m, PositionValue: 100m, Leverage: 2m, MarkPrice: 100m,
            BreakEvenPrice: null, LiquidationPrice: null, UnrealizedPnl: 0m,
            TakeProfit: null, StopLoss: null, TrailingStop: null,
            RiskId: 1, RiskLimitValue: null, CreatedTime: null, UpdatedTime: null,
            PositionIdx: positionIdx);

    private static Position CreateTrackedPosition(
        ExchangeAccountId accountId,
        string symbol = "BTCUSDT",
        MarketCategory category = MarketCategory.Linear,
        PositionSide side = PositionSide.Long,
        decimal size = 1m,
        int positionIdx = 0,
        DateTimeOffset? at = null)
    {
        var key = ExchangePositionKey.Create(accountId, InstrumentId.From(symbol), side, positionIdx);
        var t = at ?? T0;
        return Position.Create(key, category, size, t, t, averageEntryPrice: 100m, leverage: 2m);
    }

    [Fact]
    public void Complete_Empty_Observation_Closes_Missing_Position_In_Scope()
    {
        var position = CreateTrackedPosition(AccountA);
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, null, T0.AddMinutes(1), []);

        var result = PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Closed);
        result.Changes.Should().ContainSingle(c => c.Kind == PositionChangeKind.Closed);
    }

    [Fact]
    public void Failed_Empty_Observation_Marks_Unknown_Not_Closed()
    {
        var position = CreateTrackedPosition(AccountA);
        var observation = OpenPositionsObservation.Failed(MarketCategory.Linear, null, T0.AddMinutes(1), "boom");

        PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
    }

    [Fact]
    public void Partial_Empty_Observation_Does_Not_Close()
    {
        var position = CreateTrackedPosition(AccountA);
        var observation = OpenPositionsObservation.Partial(MarketCategory.Linear, null, T0.AddMinutes(1), []);

        PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        position.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
    }

    [Fact]
    public void Complete_Linear_Observation_Does_Not_Close_Inverse_Position()
    {
        var linear = CreateTrackedPosition(AccountA, symbol: "BTCUSDT", category: MarketCategory.Linear);
        var inverse = CreateTrackedPosition(AccountA, symbol: "BTCUSD", category: MarketCategory.Inverse);
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, null, T0.AddMinutes(1), []);

        PositionReconciler.Reconcile(AccountA, [linear, inverse], observation, T0.AddMinutes(1), StaleAfter);

        linear.TrackingState.Should().Be(PositionTrackingState.Closed);
        inverse.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Complete_Symbol_Scoped_Observation_Does_Not_Close_Other_Symbol()
    {
        var btc = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        var eth = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, "BTCUSDT", T0.AddMinutes(1), []);

        PositionReconciler.Reconcile(AccountA, [btc, eth], observation, T0.AddMinutes(1), StaleAfter);

        btc.TrackingState.Should().Be(PositionTrackingState.Closed);
        eth.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Observation_For_Account_A_Does_Not_Affect_Account_B()
    {
        var positionA = CreateTrackedPosition(AccountA);
        var positionB = CreateTrackedPosition(AccountB);
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, null, T0.AddMinutes(1), []);

        PositionReconciler.Reconcile(AccountA, [positionA, positionB], observation, T0.AddMinutes(1), StaleAfter);

        positionA.TrackingState.Should().Be(PositionTrackingState.Closed);
        positionB.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Closed_Position_Observed_Again_Creates_New_PositionId()
    {
        var closed = CreateTrackedPosition(AccountA);
        closed.Close(T0.AddMinutes(1));

        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(2), [CreateOpenPosition()]);

        var result = PositionReconciler.Reconcile(AccountA, [closed], observation, T0.AddMinutes(2), StaleAfter);

        result.NewPositions.Should().ContainSingle();
        result.NewPositions[0].Id.Should().NotBe(closed.Id);
        closed.TrackingState.Should().Be(PositionTrackingState.Closed);
    }

    [Fact]
    public void Observation_Older_Than_Previous_Lifecycle_Closure_Does_Not_Create_New_Position()
    {
        var closed = CreateTrackedPosition(AccountA);
        closed.Close(T0.AddMinutes(2));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1), [CreateOpenPosition()]);

        var result = PositionReconciler.Reconcile(AccountA, [closed], observation, T0.AddMinutes(2), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Observation_At_Previous_Lifecycle_Closure_Does_Not_Create_New_Position()
    {
        var closed = CreateTrackedPosition(AccountA);
        closed.Close(T0.AddMinutes(2));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(2), [CreateOpenPosition()]);

        var result = PositionReconciler.Reconcile(AccountA, [closed], observation, T0.AddMinutes(2), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void New_Observed_Position_Without_Existing_Match_Is_Created()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0, [CreateOpenPosition()]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0, StaleAfter);

        result.NewPositions.Should().ContainSingle();
        result.NewPositions[0].TrackingState.Should().Be(PositionTrackingState.Active);
        result.Changes.Should().ContainSingle(c => c.Kind == PositionChangeKind.New);
    }

    [Fact]
    public void Existing_Active_Position_Is_Updated_Not_Recreated()
    {
        var position = CreateTrackedPosition(AccountA, size: 1m);
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1), [CreateOpenPosition(size: 2m)]);

        var result = PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        position.Size.Should().Be(2m);
        result.Changes.Should().ContainSingle(c => c.Kind == PositionChangeKind.Increased);
    }

    [Fact]
    public void Unmappable_Position_In_An_Otherwise_Complete_Observation_Prevents_Closing()
    {
        var tracked = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var invalid = CreateOpenPosition(symbol: "BADCOIN", side: PositionSide.Unknown);
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1), [invalid]);

        var result = PositionReconciler.Reconcile(AccountA, [tracked], observation, T0.AddMinutes(1), StaleAfter);

        tracked.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        tracked.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Complete_Observation_With_Position_From_Different_Category_Does_Not_Infer_Closed()
    {
        var linear = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1),
            [CreateOpenPosition(category: MarketCategory.Inverse)]);

        var result = PositionReconciler.Reconcile(AccountA, [linear], observation, T0.AddMinutes(1), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.Warnings.Should().NotBeEmpty();
        linear.TrackingState.Should().Be(PositionTrackingState.Unknown);
    }

    [Fact]
    public void Complete_Symbol_Scoped_Observation_With_Other_Symbol_Does_Not_Infer_Closed()
    {
        var btc = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, "BTCUSDT", T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "ETHUSDT")]);

        var result = PositionReconciler.Reconcile(AccountA, [btc], observation, T0.AddMinutes(1), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.Warnings.Should().NotBeEmpty();
        btc.TrackingState.Should().Be(PositionTrackingState.Unknown);
    }

    [Fact]
    public void Old_Failed_Observation_Does_Not_Override_Newer_Confirmed_State()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddHours(1));
        var observation = OpenPositionsObservation.Failed(
            MarketCategory.Linear, null, T0.AddMinutes(30), "boom");

        var act = () => PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddHours(1), StaleAfter);

        act.Should().Throw<InvalidOperationException>();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Old_Partial_Observation_Does_Not_Override_Newer_Confirmed_State()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddHours(1));
        var observation = OpenPositionsObservation.Partial(
            MarketCategory.Linear, null, T0.AddMinutes(30), []);

        var act = () => PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddHours(1), StaleAfter);

        act.Should().Throw<InvalidOperationException>();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void RefreshFreshness_Is_Applied_Regardless_Of_Observation_Scope()
    {
        var position = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, "BTCUSDT", T0.AddHours(2), []);

        PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddHours(2), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Stale);
    }

    [Fact]
    public void Reconcile_Account_A_Does_Not_Mark_Account_B_Stale()
    {
        var positionA = CreateTrackedPosition(AccountA);
        var positionB = CreateTrackedPosition(AccountB);
        var observation = OpenPositionsObservation.Partial(MarketCategory.Inverse, null, T0.AddHours(2), []);

        PositionReconciler.Reconcile(AccountA, [positionA, positionB], observation, T0.AddHours(2), StaleAfter);

        positionA.TrackingState.Should().Be(PositionTrackingState.Stale);
        positionB.TrackingState.Should().Be(PositionTrackingState.Active);
    }
}
