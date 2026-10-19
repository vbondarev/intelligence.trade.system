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
        int positionIdx = 0,
        string? settlementAsset = "USDT") =>
        new(symbol, category, side, PositionStatus.Normal, size,
            AvgPrice: 100m, PositionValue: 100m, Leverage: 2m, MarkPrice: 100m,
            BreakEvenPrice: null, LiquidationPrice: null, UnrealizedPnl: 0m,
            TakeProfit: null, StopLoss: null, TrailingStop: null,
            RiskId: 1, RiskLimitValue: null, CreatedTime: null, UpdatedTime: null,
            PositionIdx: positionIdx,
            SettlementAsset: settlementAsset is null ? null : SettlementAsset.From(settlementAsset));

    private static Position CreateTrackedPosition(
        ExchangeAccountId accountId,
        string symbol = "BTCUSDT",
        MarketCategory category = MarketCategory.Linear,
        PositionSide side = PositionSide.Long,
        decimal size = 1m,
        int positionIdx = 0,
        DateTimeOffset? at = null,
        string settlementAsset = "USDT")
    {
        var key = ExchangePositionKey.Create(accountId, InstrumentId.From(symbol), side, positionIdx);
        var t = at ?? T0;
        return Position.Create(
            key, category, SettlementAsset.From(settlementAsset), size, t, t, averageEntryPrice: 100m, leverage: 2m);
    }

    [Fact]
    public void Complete_Empty_Observation_Closes_Missing_Position_In_Scope()
    {
        var position = CreateTrackedPosition(AccountA);
        var observation = OpenPositionsObservation.Complete(MarketCategory.Linear, null, T0.AddMinutes(1), []);

        var result = PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Closed);
        result.Changes.Should().ContainSingle(c => c.Kind == PositionChangeKind.Closed);
        result.IsFullyReconciled.Should().BeTrue();
    }

    [Fact]
    public void Failed_Empty_Observation_Marks_Unknown_Not_Closed()
    {
        var position = CreateTrackedPosition(AccountA);
        var observation = OpenPositionsObservation.Failed(MarketCategory.Linear, null, T0.AddMinutes(1), "boom");

        var result = PositionReconciler.Reconcile(
            AccountA, [position], observation, T0.AddMinutes(1), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.PositionsToPersist.Should().ContainSingle().Which.Should().BeSameAs(position);
        result.Changes.Should().ContainSingle(change => change.Kind == PositionChangeKind.MarkedUnknown);
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Partial_Observation_Updates_Observed_And_Marks_Missing_Unknown()
    {
        var observed = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        var missing = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var observation = OpenPositionsObservation.Partial(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "BTCUSDT", size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [observed, missing], observation, T0.AddMinutes(1), StaleAfter);

        observed.Size.Should().Be(2m);
        observed.TrackingState.Should().Be(PositionTrackingState.Active);
        missing.TrackingState.Should().Be(PositionTrackingState.Unknown);
        missing.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        result.PositionsToPersist.Should().Contain(observed).And.Contain(missing);
        result.IsFullyReconciled.Should().BeFalse();
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
    public void Older_Active_Observation_Is_Ignored_Without_History()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddMinutes(2));
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0.AddMinutes(1),
                [CreateOpenPosition()]),
            T0.AddMinutes(2),
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.LastObservedAt.Should().Be(T0.AddMinutes(2));
        position.Changes.Should().ContainSingle();
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
    }

    [Fact]
    public void Equal_Active_Observation_With_Identical_Material_State_Is_Idempotent_And_Fully_Reconciled()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                [CreateOpenPosition(size: 1m)]),
            observedAt,
            StaleAfter);

        position.Size.Should().Be(1m);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.Changes.Should().ContainSingle();
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeTrue();
    }

    [Fact]
    public void Equal_Timestamp_With_Conflicting_Size_Does_Not_Fully_Reconcile()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                [CreateOpenPosition(size: 2m)]),
            observedAt,
            StaleAfter);

        position.Size.Should().Be(1m);
        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.Changes.Should().ContainSingle();
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Equal_Timestamp_Empty_Complete_Does_Not_Close_Or_Fully_Reconcile()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(MarketCategory.Linear, null, observedAt, []),
            observedAt,
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.Size.Should().Be(1m);
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Equal_Timestamp_With_Conflicting_Leverage_Does_Not_Fully_Reconcile()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var conflicting = CreateOpenPosition(size: 1m) with { Leverage = 5m };
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(MarketCategory.Linear, null, observedAt, [conflicting]),
            observedAt,
            StaleAfter);

        position.Size.Should().Be(1m);
        position.Leverage.Should().Be(2m);
        result.Changes.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Equal_Timestamp_Does_Not_Fully_Reconcile_Unknown_Position()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        position.MarkUnknown(observedAt, PositionChangeCause.PositionsObservationFailed);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                [CreateOpenPosition(size: 1m)]),
            observedAt,
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.Changes.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Older_Empty_Complete_Observation_Does_Not_Close_A_New_Position()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddMinutes(2));

        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0.AddMinutes(1),
                []),
            T0.AddMinutes(2),
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
    }

    [Fact]
    public void Out_Of_Order_Complete_Row_Prevents_Closing_Other_Missing_Positions()
    {
        var closed = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        closed.Close(T0.AddMinutes(2));
        var missing = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(2),
            [CreateOpenPosition(symbol: "BTCUSDT")]);

        var result = PositionReconciler.Reconcile(
            AccountA,
            [closed, missing],
            observation,
            T0.AddMinutes(2),
            StaleAfter);

        missing.TrackingState.Should().Be(PositionTrackingState.Unknown);
        missing.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        result.IsFullyReconciled.Should().BeFalse();
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
    public void Fresh_Observation_Does_Not_Create_Artificial_Stale_And_Recovered_Changes()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddHours(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddHours(12), [CreateOpenPosition()]);

        var result = PositionReconciler.Reconcile(AccountA, [position], observation, T0.AddHours(12), StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        position.LastObservedAt.Should().Be(T0.AddHours(12));
        result.Changes.Should().NotContain(change =>
            change.Kind == PositionChangeKind.MarkedStale ||
            change.Kind == PositionChangeKind.Recovered);
        result.Changes.Should().BeEmpty();
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
        result.IsFullyReconciled.Should().BeFalse();
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
    public void New_Position_Keeps_Observed_Settlement_Asset()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "ETHUSDC", settlementAsset: "USDC")]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(1), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
        result.IsFullyReconciled.Should().BeTrue();
    }

    [Fact]
    public void Observation_With_Same_Settlement_Asset_Updates_Existing_Position()
    {
        var tracked = CreateTrackedPosition(AccountA, symbol: "ETHUSDC", settlementAsset: "USDC");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "ETHUSDC", size: 2m, settlementAsset: "USDC")]);

        var result = PositionReconciler.Reconcile(AccountA, [tracked], observation, T0.AddMinutes(1), StaleAfter);

        tracked.Size.Should().Be(2m);
        tracked.SettlementAsset.Should().Be(SettlementAsset.From("USDC"));
        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeTrue();
    }

    [Fact]
    public void Mismatching_Settlement_Asset_Is_Not_Applied_And_Does_Not_Infer_Closed()
    {
        var tracked = CreateTrackedPosition(AccountA, symbol: "BTCUSDT", settlementAsset: "USDT");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "BTCUSDT", size: 5m, settlementAsset: "USDC")]);

        var result = PositionReconciler.Reconcile(AccountA, [tracked], observation, T0.AddMinutes(1), StaleAfter);

        tracked.Size.Should().Be(1m);
        tracked.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        tracked.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.NewPositions.Should().BeEmpty();
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Closed);
        result.Warnings.Should().Contain(warning =>
            warning.Contains("наблюдаемый актив расчёта USDC не совпадает с активом расчёта существующей позиции USDT", StringComparison.Ordinal));
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Observation_With_Unknown_Settlement_Asset_Is_Skipped_And_Does_Not_Infer_Closed()
    {
        var tracked = CreateTrackedPosition(AccountA, symbol: "ETHUSDT");
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, null, T0.AddMinutes(1),
            [CreateOpenPosition(symbol: "BTCUSDT", settlementAsset: null)]);

        var result = PositionReconciler.Reconcile(AccountA, [tracked], observation, T0.AddMinutes(1), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        tracked.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Closed);
        result.Warnings.Should().Contain(warning =>
            warning.Contains("актив расчёта не определён для области наблюдения", StringComparison.Ordinal));
        result.IsFullyReconciled.Should().BeFalse();
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
    public void Complete_Observation_With_Missing_Position_Symbol_Does_Not_Throw_Or_Infer_Closed()
    {
        var btc = CreateTrackedPosition(AccountA, symbol: "BTCUSDT");
        var invalid = CreateOpenPosition(symbol: null!);
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, "BTCUSDT", T0.AddMinutes(1), [invalid]);
        PositionReconciliationResult? result = null;

        var act = () => result = PositionReconciler.Reconcile(AccountA, [btc], observation, T0.AddMinutes(1), StaleAfter);

        act.Should().NotThrow();
        result!.NewPositions.Should().BeEmpty();
        result.Warnings.Should().NotBeEmpty();
        btc.TrackingState.Should().Be(PositionTrackingState.Unknown);
        result.IsFullyReconciled.Should().BeFalse();
    }

    [Fact]
    public void Unknown_Position_Recovers_To_Active_From_A_Valid_Observation()
    {
        var position = CreateTrackedPosition(AccountA);
        position.MarkUnknown(T0.AddMinutes(1));

        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0.AddMinutes(2),
                [CreateOpenPosition()]),
            T0.AddMinutes(2),
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        result.Changes.Should().ContainSingle(change => change.Kind == PositionChangeKind.Recovered);
    }

    [Fact]
    public void Stale_Position_Recovers_To_Active_From_A_Valid_Observation()
    {
        var position = CreateTrackedPosition(AccountA);
        position.RefreshFreshness(T0.AddHours(2), StaleAfter);

        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0.AddHours(3),
                [CreateOpenPosition()]),
            T0.AddHours(3),
            StaleAfter);

        position.TrackingState.Should().Be(PositionTrackingState.Active);
        result.Changes.Should().ContainSingle(change => change.Kind == PositionChangeKind.Recovered);
    }

    [Fact]
    public void Old_Failed_Observation_Does_Not_Override_Newer_Confirmed_State()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddHours(1));
        var observation = OpenPositionsObservation.Failed(
            MarketCategory.Linear, null, T0.AddMinutes(30), "boom");

        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            observation,
            T0.AddHours(1),
            StaleAfter);

        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Fact]
    public void Old_Partial_Observation_Does_Not_Override_Newer_Confirmed_State()
    {
        var position = CreateTrackedPosition(AccountA, at: T0.AddHours(1));
        var observation = OpenPositionsObservation.Partial(
            MarketCategory.Linear, null, T0.AddMinutes(30), []);

        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            observation,
            T0.AddHours(1),
            StaleAfter);

        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        position.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    [Theory]
    [InlineData(OpenPositionsObservationStatus.Complete)]
    [InlineData(OpenPositionsObservationStatus.Partial)]
    [InlineData(OpenPositionsObservationStatus.Failed)]
    public void Observation_Older_Than_The_Latest_History_Change_Is_Ignored(OpenPositionsObservationStatus status)
    {
        var position = CreateTrackedPosition(AccountA);
        var markedAt = T0.AddMinutes(30);
        position.MarkUnknown(markedAt, PositionChangeCause.PositionsObservationFailed);
        var observedAt = T0.AddMinutes(20);
        var observation = status switch
        {
            OpenPositionsObservationStatus.Complete => OpenPositionsObservation.Complete(
                MarketCategory.Linear, null, observedAt, [CreateOpenPosition(size: 2m)]),
            OpenPositionsObservationStatus.Partial => OpenPositionsObservation.Partial(
                MarketCategory.Linear, null, observedAt, []),
            _ => OpenPositionsObservation.Failed(MarketCategory.Linear, null, observedAt, "boom"),
        };

        var result = PositionReconciler.Reconcile(AccountA, [position], observation, markedAt, StaleAfter);

        result.IsFullyReconciled.Should().BeFalse();
        result.Changes.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        position.TrackingState.Should().Be(PositionTrackingState.Unknown);
        position.Size.Should().Be(1m);
        position.Changes.Select(change => change.OccurredAt).Should().Equal(T0, markedAt);
    }

    [Fact]
    public void Closed_Lifecycle_Observed_After_The_Evaluation_Time_Does_Not_Block_Freshness()
    {
        var closed = CreateTrackedPosition(AccountA, at: T0.AddMinutes(10));
        closed.Close(T0.AddMinutes(10), PositionChangeCause.MissingFromCompleteObservation);
        var active = CreateTrackedPosition(AccountA, symbol: "ETHUSDT", at: T0);
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear, "ETHUSDT", T0.AddMinutes(5), [CreateOpenPosition("ETHUSDT")]);

        var result = PositionReconciler.Reconcile(
            AccountA,
            [closed, active],
            observation,
            T0.AddMinutes(5),
            StaleAfter);

        result.IsFullyReconciled.Should().BeTrue();
        result.PositionsToPersist.Should().Equal(active);
        closed.Changes.Should().HaveCount(2);
        active.LastObservedAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public void Older_Observation_Still_Refreshes_Stale_Freshness()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0,
                []),
            observedAt.AddMinutes(20),
            staleAfter: TimeSpan.FromMinutes(5));

        position.TrackingState.Should().Be(PositionTrackingState.Stale);
        position.Size.Should().Be(1m);
        result.Changes.Should().ContainSingle(change =>
            change.Kind == PositionChangeKind.MarkedStale);
        result.PositionsToPersist.Should().ContainSingle().Which.Should().BeSameAs(position);
    }

    [Fact]
    public void Equal_Observation_Still_Refreshes_Stale_Freshness()
    {
        var observedAt = T0.AddMinutes(1);
        var position = CreateTrackedPosition(AccountA, at: observedAt);
        var result = PositionReconciler.Reconcile(
            AccountA,
            [position],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                []),
            observedAt.AddMinutes(20),
            staleAfter: TimeSpan.FromMinutes(5));

        position.TrackingState.Should().Be(PositionTrackingState.Stale);
        result.Changes.Should().ContainSingle(change =>
            change.Kind == PositionChangeKind.MarkedStale);
        result.PositionsToPersist.Should().ContainSingle().Which.Should().BeSameAs(position);
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

    /// <summary>R-208-05: закрытие Long защищает физический слот от более старого Short.</summary>
    [Fact]
    public void Opposite_Side_Older_Than_Physical_Slot_Closure_Does_Not_Create_Position()
    {
        var closedLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0);
        closedLong.Close(T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(6),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [closedLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
        result.Warnings.Should().NotBeEmpty();
        closedLong.TrackingState.Should().Be(PositionTrackingState.Closed);
    }

    /// <summary>R-208-05: Short в момент закрытия Long не создаёт lifecycle.</summary>
    [Fact]
    public void Opposite_Side_At_Physical_Slot_Closure_Does_Not_Create_Position()
    {
        var closedLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0);
        closedLong.Close(T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(10),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [closedLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>R-208-05 / G5: действительно новое Short после закрытия Long допускается.</summary>
    [Fact]
    public void Opposite_Side_Newer_Than_Physical_Slot_Closure_Creates_Position()
    {
        var closedLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0);
        closedLong.Close(T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(11),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [closedLong], observation, T0.AddMinutes(11), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Short);
        result.IsFullyReconciled.Should().BeTrue();
    }

    /// <summary>R-208-05: закрытие idx=0 не блокирует противоположную сторону другого idx.</summary>
    [Fact]
    public void Opposite_Side_On_Different_PositionIdx_Is_Not_Blocked_By_Closure()
    {
        var closedLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, positionIdx: 1, at: T0);
        closedLong.Close(T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(6),
            [CreateOpenPosition(side: PositionSide.Short, positionIdx: 2, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [closedLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionIdx.Should().Be(2);
        result.IsFullyReconciled.Should().BeTrue();
    }

    /// <summary>R-208-06 / G6: равное время Long Active + REST Short не сохраняет вторую сторону.</summary>
    [Fact]
    public void Equal_Timestamp_Opposite_Side_On_Same_Slot_Does_Not_Create_Conflicting_Lifecycle()
    {
        var activeLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(10),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.Changes.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
        activeLong.TrackingState.Should().Be(PositionTrackingState.Active);
        activeLong.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Long);
        activeLong.Size.Should().Be(1m);
    }

    /// <summary>R-208-06: более старый Short не создаётся поверх активного Long.</summary>
    [Fact]
    public void Older_Opposite_Side_Does_Not_Create_Lifecycle_Over_Active_Slot()
    {
        var activeLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(6),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
        activeLong.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    /// <summary>R-208-06: конфликт слота не блокирует достоверное открытие другого символа.</summary>
    [Fact]
    public void Slot_Conflict_Does_Not_Block_Independent_Symbol()
    {
        var activeLong = CreateTrackedPosition(AccountA, symbol: "BTCUSDT", side: PositionSide.Long, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(10),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(10), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.InstrumentId.Value.Should().Be("ETHUSDT");
        result.IsFullyReconciled.Should().BeFalse();
        activeLong.TrackingState.Should().Be(PositionTrackingState.Active);
    }

    /// <summary>R-208-07 / G7: Complete с Long и Short одного one-way idx=0 не подтверждает полноту.</summary>
    [Fact]
    public void Complete_With_Conflicting_One_Way_Sides_Does_Not_Create_Either_Lifecycle()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 1m, positionIdx: 0),
                CreateOpenPosition(side: PositionSide.Short, size: 2m, positionIdx: 0),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
        result.Warnings.Should().NotBeEmpty();
    }

    /// <summary>R-208-07: Partial с противоречащими сторонами one-way слота не создаёт lifecycle.</summary>
    [Fact]
    public void Partial_With_Conflicting_One_Way_Sides_Does_Not_Create_Either_Lifecycle()
    {
        var observation = OpenPositionsObservation.Partial(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 1m),
                CreateOpenPosition(side: PositionSide.Short, size: 2m),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>R-208-07 / G8: hedge Long idx=1 и Short idx=2 принимаются.</summary>
    [Fact]
    public void Complete_Hedge_Long_And_Short_On_Different_Idx_Are_Accepted()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, positionIdx: 1, size: 1m),
                CreateOpenPosition(side: PositionSide.Short, positionIdx: 2, size: 2m),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().HaveCount(2);
        result.IsFullyReconciled.Should().BeTrue();
    }

    /// <summary>R-208-07: повтор идентичной строки одного слота не создаёт второй lifecycle.</summary>
    [Fact]
    public void Duplicate_Identical_Row_Is_Skipped_Without_Second_Lifecycle()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 1m),
                CreateOpenPosition(side: PositionSide.Long, size: 1m),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().ContainSingle();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>G5: Complete side-change новее активного Long закрывает Long и открывает Short.</summary>
    [Fact]
    public void Newer_Complete_Opposite_Side_Replaces_Active_Lifecycle()
    {
        var activeLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(11),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(11), StaleAfter);

        activeLong.TrackingState.Should().Be(PositionTrackingState.Closed);
        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionSide.Should().Be(PositionSide.Short);
        result.IsFullyReconciled.Should().BeTrue();
    }

    /// <summary>
    /// R-208-08 / A1: конфликт сторон ETH не допускает открытие BTC Short без согласованного закрытия Long.
    /// </summary>
    [Fact]
    public void Side_Change_Blocked_When_Other_Symbol_Has_Conflicting_Sides()
    {
        var activeLong = CreateTrackedPosition(
            AccountA, symbol: "BTCUSDT", side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Short, size: 4m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().BeEmpty(
            "BTC Short нельзя создать, пока конфликт ETH запрещает закрытие BTC Long");
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.New);
        activeLong.ClosedAt.Should().BeNull();
        activeLong.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        result.IsFullyReconciled.Should().BeFalse();
        result.NewPositions
            .Concat([activeLong])
            .Count(position =>
                position.TrackingState != PositionTrackingState.Closed &&
                position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
                position.ExchangePositionKey.PositionIdx == 0)
            .Should().Be(1);
    }

    /// <summary>
    /// R-208-08 / A2: идентичный дубликат ETH создаёт hasMappingIssues и блокирует смену стороны BTC.
    /// </summary>
    [Fact]
    public void Side_Change_Blocked_When_Other_Symbol_Has_Identical_Duplicate()
    {
        var activeLong = CreateTrackedPosition(
            AccountA, symbol: "BTCUSDT", side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().NotContain(
            position => position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT",
            "BTC Short нельзя открыть, пока дубликат ETH запрещает закрытие Long");
        activeLong.ClosedAt.Should().BeNull();
        activeLong.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-09 / B1: противоречащие дубликаты одного ключа не применяют первую строку.
    /// </summary>
    [Fact]
    public void Conflicting_Duplicates_Do_Not_Apply_First_Row()
    {
        var activeLong = CreateTrackedPosition(
            AccountA, side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m),
                CreateOpenPosition(side: PositionSide.Long, size: 3m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        activeLong.Size.Should().Be(1m);
        activeLong.LastObservedAt.Should().Be(T0.AddMinutes(10));
        activeLong.TrackingState.Should().Be(PositionTrackingState.Active);
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Increased);
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Reduced);
        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-09 / B2: обратный порядок противоречащих дубликатов даёт тот же persisted state.
    /// </summary>
    [Fact]
    public void Conflicting_Duplicates_Are_Order_Independent()
    {
        var firstOrder = CreateTrackedPosition(
            AccountA, side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var secondOrder = CreateTrackedPosition(
            AccountA, side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observedAt = T0.AddMinutes(20);

        var resultAsc = PositionReconciler.Reconcile(
            AccountA,
            [firstOrder],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                [
                    CreateOpenPosition(side: PositionSide.Long, size: 2m),
                    CreateOpenPosition(side: PositionSide.Long, size: 3m),
                ]),
            observedAt,
            StaleAfter);
        var resultDesc = PositionReconciler.Reconcile(
            AccountA,
            [secondOrder],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                observedAt,
                [
                    CreateOpenPosition(side: PositionSide.Long, size: 3m),
                    CreateOpenPosition(side: PositionSide.Long, size: 2m),
                ]),
            observedAt,
            StaleAfter);

        firstOrder.Size.Should().Be(1m);
        secondOrder.Size.Should().Be(1m);
        firstOrder.LastObservedAt.Should().Be(secondOrder.LastObservedAt);
        firstOrder.TrackingState.Should().Be(secondOrder.TrackingState);
        resultAsc.NewPositions.Should().BeEmpty();
        resultDesc.NewPositions.Should().BeEmpty();
        resultAsc.IsFullyReconciled.Should().BeFalse();
        resultDesc.IsFullyReconciled.Should().BeFalse();
        resultAsc.Changes.Select(change => change.Kind)
            .Should().BeEquivalentTo(resultDesc.Changes.Select(change => change.Kind));
    }

    /// <summary>
    /// R-208-08 / A4: Partial не выполняет смену стороны физического слота.
    /// </summary>
    [Fact]
    public void Partial_Observation_Does_Not_Replace_Active_Lifecycle_On_Same_Slot()
    {
        var activeLong = CreateTrackedPosition(AccountA, side: PositionSide.Long, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Partial(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [CreateOpenPosition(side: PositionSide.Short, size: 2m)]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        activeLong.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        activeLong.ClosedAt.Should().BeNull();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-09 / B4: конфликт BTC не блокирует достоверное открытие независимого ETH.
    /// </summary>
    [Fact]
    public void Conflicting_Btc_Duplicates_Do_Not_Block_Independent_Eth()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Long, size: 2m),
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Long, size: 3m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 4m),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.InstrumentId.Value.Should().Be("ETHUSDT");
        result.NewPositions.Should().NotContain(position =>
            position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT");
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-09 / B5: конфликт ETH не отменяет допустимое обновление размера BTC без смены стороны.
    /// </summary>
    [Fact]
    public void Eth_Conflict_Does_Not_Block_Independent_Btc_Size_Update()
    {
        var activeLong = CreateTrackedPosition(
            AccountA, symbol: "BTCUSDT", side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Long, size: 2m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Short, size: 4m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        activeLong.Size.Should().Be(2m);
        activeLong.TrackingState.Should().Be(PositionTrackingState.Active);
        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-09 / B8: повтор конфликтующего Complete не накапливает историю размера.
    /// </summary>
    [Fact]
    public void Repeated_Conflicting_Complete_Does_Not_Accumulate_Size_History()
    {
        var activeLong = CreateTrackedPosition(
            AccountA, side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m),
                CreateOpenPosition(side: PositionSide.Long, size: 3m),
            ]);

        PositionReconciler.Reconcile(AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);
        var historyAfterFirst = activeLong.Changes.Count;
        PositionReconciler.Reconcile(AccountA, [activeLong], observation, T0.AddMinutes(20), StaleAfter);

        activeLong.Size.Should().Be(1m);
        activeLong.Changes.Should().HaveCount(historyAfterFirst);
        activeLong.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Increased);
    }

    /// <summary>
    /// R-208-10 / R10-A: BTC Short раньше ETH с ClosedAt=T30 не создаёт противоположный lifecycle.
    /// </summary>
    [Fact]
    public void Side_Change_Blocked_When_Later_Row_Conflicts_With_Closed_Lifecycle_Btc_Then_Eth()
    {
        var (btcLong, ethClosed, result) = ReconcileSideChangeWithClosedEthConflict(
            btcShortFirst: true);

        AssertSideChangeBlockedByClosedEthConflict(btcLong, ethClosed, result);
    }

    /// <summary>
    /// R-208-10 / R10-B: обратный порядок строк даёт тот же persisted business state.
    /// </summary>
    [Fact]
    public void Side_Change_Blocked_When_Later_Row_Conflicts_With_Closed_Lifecycle_Eth_Then_Btc()
    {
        var (btcLong, ethClosed, result) = ReconcileSideChangeWithClosedEthConflict(
            btcShortFirst: false);

        AssertSideChangeBlockedByClosedEthConflict(btcLong, ethClosed, result);
    }

    /// <summary>
    /// R-208-12 / A1: Long без SettlementAsset + Short USDT одного idx=0 не создаёт lifecycle.
    /// </summary>
    [Fact]
    public void Rejected_Missing_Settlement_Hides_Side_Conflict_Long_Then_Short()
    {
        var result = ReconcileMissingSettlementSideConflict(missingSettlementFirst: true);

        AssertAmbiguousPhysicalSlotRejected(result);
    }

    /// <summary>
    /// R-208-12 / A2: обратный порядок строк даёт тот же результат.
    /// </summary>
    [Fact]
    public void Rejected_Missing_Settlement_Hides_Side_Conflict_Short_Then_Long()
    {
        var result = ReconcileMissingSettlementSideConflict(missingSettlementFirst: false);

        AssertAmbiguousPhysicalSlotRejected(result);
    }

    /// <summary>
    /// R-208-13 / A3: отклонённый USDC-дубликат не позволяет применить USDT Size=2.
    /// </summary>
    [Fact]
    public void Rejected_Settlement_Duplicate_Does_Not_Update_Tracked_Usdt_Then_Usdc()
    {
        var (tracked, result) = ReconcileRejectedSettlementDuplicateAgainstTracked(usdtFirst: true);

        AssertTrackedUnchangedByAmbiguousSettlementDuplicate(tracked, result);
    }

    /// <summary>
    /// R-208-13 / A4: обратный порядок REST-строк сохраняет Size=1.
    /// </summary>
    [Fact]
    public void Rejected_Settlement_Duplicate_Does_Not_Update_Tracked_Usdc_Then_Usdt()
    {
        var (tracked, result) = ReconcileRejectedSettlementDuplicateAgainstTracked(usdtFirst: false);

        AssertTrackedUnchangedByAmbiguousSettlementDuplicate(tracked, result);
    }

    /// <summary>
    /// R-208-11 / R11-A: дубликаты одного ключа с USDT→USDC не создают lifecycle.
    /// </summary>
    [Fact]
    public void Duplicate_Rows_With_Conflicting_SettlementAsset_Do_Not_Create_Lifecycle_Usdt_Then_Usdc()
    {
        var result = ReconcileConflictingSettlementDuplicates(usdtFirst: true);

        AssertConflictingSettlementDuplicatesRejected(result);
    }

    /// <summary>
    /// R-208-11 / R11-B: обратный порядок USDC→USDT даёт тот же результат.
    /// </summary>
    [Fact]
    public void Duplicate_Rows_With_Conflicting_SettlementAsset_Do_Not_Create_Lifecycle_Usdc_Then_Usdt()
    {
        var result = ReconcileConflictingSettlementDuplicates(usdtFirst: false);

        AssertConflictingSettlementDuplicatesRejected(result);
    }

    private static (
        Position BtcLong,
        Position EthClosed,
        PositionReconciliationResult Result) ReconcileSideChangeWithClosedEthConflict(bool btcShortFirst)
    {
        var btcLong = CreateTrackedPosition(
            AccountA, symbol: "BTCUSDT", side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        var ethClosed = CreateTrackedPosition(
            AccountA, symbol: "ETHUSDT", side: PositionSide.Long, size: 1m, at: T0.AddMinutes(10));
        ethClosed.Close(T0.AddMinutes(30));

        OpenPosition[] rows = btcShortFirst
            ?
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
            ]
            :
            [
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m),
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m),
            ];

        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            rows);
        var result = PositionReconciler.Reconcile(
            AccountA, [btcLong, ethClosed], observation, T0.AddMinutes(20), StaleAfter);
        return (btcLong, ethClosed, result);
    }

    private static void AssertSideChangeBlockedByClosedEthConflict(
        Position btcLong,
        Position ethClosed,
        PositionReconciliationResult result)
    {
        result.NewPositions.Should().BeEmpty(
            "противоположная сторона BTC не создаётся, пока закрытие Long не доказано");
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.New);
        result.Changes.Should().NotContain(change =>
            change.Kind == PositionChangeKind.Closed &&
            change.PositionId == btcLong.Id);
        btcLong.ClosedAt.Should().BeNull();
        btcLong.TrackingState.Should().NotBe(PositionTrackingState.Closed);
        ethClosed.TrackingState.Should().Be(PositionTrackingState.Closed);
        ethClosed.ClosedAt.Should().Be(T0.AddMinutes(30));
        result.IsFullyReconciled.Should().BeFalse();
        result.NewPositions
            .Concat([btcLong])
            .Count(position =>
                position.TrackingState != PositionTrackingState.Closed &&
                position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT" &&
                position.ExchangePositionKey.PositionIdx == 0)
            .Should().Be(1);
    }

    private static PositionReconciliationResult ReconcileMissingSettlementSideConflict(
        bool missingSettlementFirst)
    {
        OpenPosition[] rows = missingSettlementFirst
            ?
            [
                CreateOpenPosition(side: PositionSide.Long, size: 1m, settlementAsset: null),
                CreateOpenPosition(side: PositionSide.Short, size: 2m, settlementAsset: "USDT"),
            ]
            :
            [
                CreateOpenPosition(side: PositionSide.Short, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 1m, settlementAsset: null),
            ];

        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            rows);
        return PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);
    }

    private static void AssertAmbiguousPhysicalSlotRejected(PositionReconciliationResult result)
    {
        result.NewPositions.Should().BeEmpty(
            "неоднозначный физический слот не создаёт lifecycle ни по одной стороне");
        result.PositionsToPersist.Should().BeEmpty();
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.New);
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.Closed);
        result.IsFullyReconciled.Should().BeFalse();
    }

    private static (Position Tracked, PositionReconciliationResult Result)
        ReconcileRejectedSettlementDuplicateAgainstTracked(bool usdtFirst)
    {
        var tracked = CreateTrackedPosition(
            AccountA,
            side: PositionSide.Long,
            size: 1m,
            at: T0.AddMinutes(10),
            settlementAsset: "USDT");
        OpenPosition[] rows = usdtFirst
            ?
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 3m, settlementAsset: "USDC"),
            ]
            :
            [
                CreateOpenPosition(side: PositionSide.Long, size: 3m, settlementAsset: "USDC"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
            ];

        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            rows);
        var result = PositionReconciler.Reconcile(
            AccountA, [tracked], observation, T0.AddMinutes(20), StaleAfter);
        return (tracked, result);
    }

    private static void AssertTrackedUnchangedByAmbiguousSettlementDuplicate(
        Position tracked,
        PositionReconciliationResult result)
    {
        tracked.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        tracked.Size.Should().Be(1m);
        tracked.LastObservedAt.Should().Be(T0.AddMinutes(10));
        tracked.TrackingState.Should().Be(PositionTrackingState.Active);
        result.NewPositions.Should().BeEmpty();
        result.PositionsToPersist.Should().BeEmpty();
        result.Changes.Should().NotContain(change =>
            change.Kind == PositionChangeKind.Increased ||
            change.Kind == PositionChangeKind.Reduced ||
            change.Kind == PositionChangeKind.New ||
            change.Kind == PositionChangeKind.Closed);
        result.IsFullyReconciled.Should().BeFalse();
    }

    private static PositionReconciliationResult ReconcileConflictingSettlementDuplicates(bool usdtFirst)
    {
        OpenPosition[] rows = usdtFirst
            ?
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDC"),
            ]
            :
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDC"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
            ];

        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            rows);
        return PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);
    }

    private static void AssertConflictingSettlementDuplicatesRejected(PositionReconciliationResult result)
    {
        result.NewPositions.Should().BeEmpty(
            "дубликаты с разным SettlementAsset не выбирают произвольный актив расчёта");
        result.Changes.Should().NotContain(change => change.Kind == PositionChangeKind.New);
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-11 / R11-C: идентичный SettlementAsset сохраняет консервативную семантику дубликата.
    /// </summary>
    [Fact]
    public void Identical_SettlementAsset_Duplicates_Keep_Conservative_Incompleteness()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-11 / R11-F: строка без SettlementAsset рядом с полной делает слот неоднозначным.
    /// </summary>
    [Fact]
    public void Duplicate_With_Missing_SettlementAsset_Does_Not_Create_Lifecycle()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: null),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().BeEmpty();
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-12 / A7: конфликт BTC не блокирует достоверное открытие независимого ETH.
    /// </summary>
    [Fact]
    public void Ambiguous_Btc_Slot_Does_Not_Block_Independent_Eth()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Long, size: 1m, settlementAsset: null),
                CreateOpenPosition(symbol: "BTCUSDT", side: PositionSide.Short, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(symbol: "ETHUSDT", side: PositionSide.Long, size: 3m, settlementAsset: "USDT"),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.InstrumentId.Value.Should().Be("ETHUSDT");
        result.NewPositions.Should().NotContain(position =>
            position.ExchangePositionKey.InstrumentId.Value == "BTCUSDT");
        result.IsFullyReconciled.Should().BeFalse();
    }

    /// <summary>
    /// R-208-12 / A11: противоречащие стороны с разными settlement assets не применяются.
    /// </summary>
    [Fact]
    public void Conflicting_Sides_With_Different_Settlement_Assets_Are_Not_Applied()
    {
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 1m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Short, size: 2m, settlementAsset: "USDC"),
            ]);

        var result = PositionReconciler.Reconcile(AccountA, [], observation, T0.AddMinutes(20), StaleAfter);

        AssertAmbiguousPhysicalSlotRejected(result);
    }

    /// <summary>
    /// R-208-11 / R11-G+H: повтор конфликта не копит историю; последующий чистый Complete восстанавливает.
    /// </summary>
    [Fact]
    public void Conflicting_Settlement_Then_Clean_Complete_Allows_Recovery()
    {
        var conflict = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT"),
                CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDC"),
            ]);
        PositionReconciler.Reconcile(AccountA, [], conflict, T0.AddMinutes(20), StaleAfter)
            .NewPositions.Should().BeEmpty();

        var recovered = PositionReconciler.Reconcile(
            AccountA,
            [],
            OpenPositionsObservation.Complete(
                MarketCategory.Linear,
                null,
                T0.AddMinutes(30),
                [CreateOpenPosition(side: PositionSide.Long, size: 2m, settlementAsset: "USDT")]),
            T0.AddMinutes(30),
            StaleAfter);

        recovered.NewPositions.Should().ContainSingle()
            .Which.SettlementAsset.Should().Be(SettlementAsset.From("USDT"));
        recovered.IsFullyReconciled.Should().BeTrue();
    }

    /// <summary>
    /// R-208-10 / R10-H: конфликт одного hedge-слота не повреждает независимый слот.
    /// </summary>
    [Fact]
    public void Closed_Lifecycle_Conflict_On_One_Hedge_Slot_Does_Not_Block_Other_Idx()
    {
        var closedIdx1 = CreateTrackedPosition(
            AccountA, side: PositionSide.Long, positionIdx: 1, at: T0.AddMinutes(10));
        closedIdx1.Close(T0.AddMinutes(30));
        var observation = OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            T0.AddMinutes(20),
            [
                CreateOpenPosition(side: PositionSide.Short, positionIdx: 1, size: 2m),
                CreateOpenPosition(side: PositionSide.Short, positionIdx: 2, size: 3m),
            ]);

        var result = PositionReconciler.Reconcile(
            AccountA, [closedIdx1], observation, T0.AddMinutes(20), StaleAfter);

        result.NewPositions.Should().ContainSingle()
            .Which.ExchangePositionKey.PositionIdx.Should().Be(2);
        result.NewPositions.Should().NotContain(position =>
            position.ExchangePositionKey.PositionIdx == 1);
        result.IsFullyReconciled.Should().BeFalse();
    }
}
