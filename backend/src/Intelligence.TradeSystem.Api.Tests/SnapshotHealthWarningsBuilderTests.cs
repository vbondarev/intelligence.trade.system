using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Tests.Helpers;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Unit-тесты для <see cref="SnapshotHealthWarningsBuilder"/>.
/// Работают напрямую с builder'ом — без HTTP-стека.
/// </summary>
public sealed class SnapshotHealthWarningsBuilderTests
{
    // ─── Intraday thresholds: OB=2s=2000ms, TF=5s=5000ms, Der=30s=30000ms ──
    private static readonly SectionFreshnessOptions _intradayThresholds =
        SnapshotFreshnessOptions.Default.Intraday;

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SnapshotHealthWarningsContext BuildCtx(
        AnalysisMode mode = AnalysisMode.Intraday,
        Dictionary<string, long>? sectionAgesMs = null,
        decimal stalenessProximityFactor = 0.8m) =>
        new()
        {
            Mode = mode,
            SectionAgesMs = sectionAgesMs ?? [],
            Thresholds = _intradayThresholds,
            StalenessProximityFactor = stalenessProximityFactor,
        };

    private static MarketSnapshot DefaultSnapshot() =>
        ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish);

    // ─── 6.1 Near-staleness warnings ─────────────────────────────────────────

    [Fact]
    public void OrderBook_NearStaleness_Warning_Added_When_Age_In_Proximity_Band()
    {
        // OB threshold = 2000ms; 80% = 1600ms; age = 1800ms → в зоне [1600, 2000)
        var ctx = BuildCtx(sectionAgesMs: new() { ["orderBook"] = 1800 });

        var result = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctx, result);

        result.Should().Contain("orderBook is near staleness threshold");
    }

    [Fact]
    public void OrderBook_NearStaleness_Warning_Not_Added_When_Age_Below_Proximity()
    {
        // age = 1500ms < 1600ms → вне зоны предупреждения
        var ctx = BuildCtx(sectionAgesMs: new() { ["orderBook"] = 1500 });

        var result = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctx, result);

        result.Should().NotContain("orderBook is near staleness threshold");
    }

    [Fact]
    public void OrderBook_NearStaleness_Warning_Not_Added_When_Already_Stale()
    {
        // age = 2100ms >= 2000ms → уже stale, не дублируем near-staleness
        var ctx = BuildCtx(sectionAgesMs: new() { ["orderBook"] = 2100 });

        var result = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctx, result);

        result.Should().NotContain("orderBook is near staleness threshold");
    }

    [Fact]
    public void TradeFlow_NearStaleness_Warning_Added_When_Age_In_Proximity_Band()
    {
        // TF threshold = 5000ms; 80% = 4000ms; age = 4500ms
        var ctx = BuildCtx(sectionAgesMs: new() { ["tradeFlow"] = 4500 });

        var result = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctx, result);

        result.Should().Contain("tradeFlow is near staleness threshold");
    }

    [Fact]
    public void Derivatives_NearStaleness_Warning_Added_When_Age_In_Proximity_Band()
    {
        // Der threshold = 30000ms; 80% = 24000ms; age = 27000ms
        var ctx = BuildCtx(sectionAgesMs: new() { ["derivatives"] = 27000 });

        var result = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctx, result);

        result.Should().Contain("derivatives is near staleness threshold");
    }

    [Fact]
    public void NearStaleness_ProximityFactor_09_Raises_Threshold()
    {
        // При factor=0.9: порог = 1800ms; age=1700ms < 1800ms → НЕТ warning
        // При factor=0.8: порог = 1600ms; age=1700ms >= 1600ms → ЕСТЬ warning
        var ctxLoose = BuildCtx(sectionAgesMs: new() { ["orderBook"] = 1700 }, stalenessProximityFactor: 0.9m);
        var ctxNormal = BuildCtx(sectionAgesMs: new() { ["orderBook"] = 1700 }, stalenessProximityFactor: 0.8m);

        var resultLoose = new List<string>();
        var resultNormal = new List<string>();
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctxLoose, resultLoose);
        SnapshotHealthWarningsBuilder.AddNearStalenessWarnings(ctxNormal, resultNormal);

        resultLoose.Should().NotContain("orderBook is near staleness threshold",
            because: "при factor=0.9 и age=1700ms порог=1800ms → вне зоны");
        resultNormal.Should().Contain("orderBook is near staleness threshold",
            because: "при factor=0.8 и age=1700ms порог=1600ms → в зоне");
    }

    // ─── 6.2 Low volume ──────────────────────────────────────────────────────

    [Fact]
    public void LowVolume_Warning_Added_When_Any_Primary_TF_Has_VolumeRatio_Below_Threshold()
    {
        var snapshot = DefaultSnapshot() with
        {
            M15 = DefaultSnapshot().M15 with { VolumeRatio = 0.3m },
        };
        var ctx = BuildCtx();
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddLowVolumeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().Contain("low volume on primary timeframes");
    }

    [Fact]
    public void LowVolume_Warning_Not_Added_When_All_Primary_TFs_Have_Normal_VolumeRatio()
    {
        var snapshot = DefaultSnapshot(); // VolumeRatio = 1.1 везде
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddLowVolumeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().NotContain("low volume on primary timeframes");
    }

    [Fact]
    public void LowVolume_Warning_Added_Only_Once_Even_If_Multiple_TFs_Are_Low()
    {
        var lowTf = DefaultSnapshot().M15 with { VolumeRatio = 0.2m };
        var snapshot = DefaultSnapshot() with
        {
            M15 = lowTf,
            H1 = lowTf with { Timeframe = "1h" },
            H4 = lowTf with { Timeframe = "4h" },
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddLowVolumeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().ContainSingle(w => w == "low volume on primary timeframes");
    }

    // ─── 6.3 Conflicting microstructure ──────────────────────────────────────

    [Fact]
    public void ConflictingMicrostructure_Warning_When_OrderBook_Positive_TradeFlow_Negative()
    {
        var sentiment = DefaultSnapshot().Sentiment with
        {
            OrderBookPressureScore = 0.2m,
            TradeFlowPressureScore = -0.2m,
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddConflictingMicrostructureWarning(sentiment, result);

        result.Should().Contain("orderBook and tradeFlow signals are conflicting");
    }

    [Fact]
    public void ConflictingMicrostructure_Warning_When_OrderBook_Negative_TradeFlow_Positive()
    {
        var sentiment = DefaultSnapshot().Sentiment with
        {
            OrderBookPressureScore = -0.15m,
            TradeFlowPressureScore = 0.10m,
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddConflictingMicrostructureWarning(sentiment, result);

        result.Should().Contain("orderBook and tradeFlow signals are conflicting");
    }

    [Fact]
    public void ConflictingMicrostructure_No_Warning_When_Both_Scores_Same_Sign()
    {
        var sentiment = DefaultSnapshot().Sentiment with
        {
            OrderBookPressureScore = 0.05m,
            TradeFlowPressureScore = 0.04m,
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddConflictingMicrostructureWarning(sentiment, result);

        result.Should().NotContain("orderBook and tradeFlow signals are conflicting");
    }

    // ─── 6.4 Directional trend with neutral regime ────────────────────────────

    [Fact]
    public void DirectionalNeutralRegime_Warning_When_Primary_TF_Bullish_And_Regime_Neutral()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish) with
        {
            Sentiment = DefaultSnapshot().Sentiment with { MarketRegime = "Neutral" },
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddDirectionalNeutralRegimeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().Contain("directional trend with neutral regime");
    }

    [Fact]
    public void DirectionalNeutralRegime_Warning_When_Primary_TF_Bearish_And_Regime_Neutral()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bearish) with
        {
            Sentiment = DefaultSnapshot().Sentiment with { MarketRegime = "Neutral" },
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddDirectionalNeutralRegimeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().Contain("directional trend with neutral regime");
    }

    [Fact]
    public void DirectionalNeutralRegime_No_Warning_When_Regime_Is_Trending()
    {
        // MarketRegime = "Trending" в стандартном снапшоте
        var snapshot = DefaultSnapshot();
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddDirectionalNeutralRegimeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().NotContain("directional trend with neutral regime");
    }

    [Fact]
    public void DirectionalNeutralRegime_No_Warning_When_Trend_Is_Sideways_And_Regime_Neutral()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Sideways) with
        {
            Sentiment = DefaultSnapshot().Sentiment with { MarketRegime = "Neutral" },
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddDirectionalNeutralRegimeWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().NotContain("directional trend with neutral regime",
            because: "Sideways тренд не является directional");
    }

    // ─── 6.5 Far from relevant level ─────────────────────────────────────────

    [Fact]
    public void FarFromLevel_Warning_When_Bullish_TF_And_Support1_Distance_Exceeds_Threshold()
    {
        var farTf = DefaultSnapshot().M15 with
        {
            Trend = MarketTrend.Bullish,
            DistanceToSupport1Pct = 2.0m,  // > 1.5
        };
        var snapshot = DefaultSnapshot() with { M15 = farTf };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddFarFromLevelWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().Contain("price is far from nearest relevant level");
    }

    [Fact]
    public void FarFromLevel_Warning_When_Bearish_TF_And_Resistance1_Distance_Exceeds_Threshold()
    {
        var farTf = DefaultSnapshot().M15 with
        {
            Trend = MarketTrend.Bearish,
            DistanceToResistance1Pct = 1.8m,  // > 1.5
        };
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bearish) with { M15 = farTf };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddFarFromLevelWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().Contain("price is far from nearest relevant level");
    }

    [Fact]
    public void FarFromLevel_No_Warning_When_Distance_Within_Threshold()
    {
        // DistanceToSupport1Pct = 0.6154 — хорошо в пределах 1.5
        var snapshot = DefaultSnapshot();
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddFarFromLevelWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().NotContain("price is far from nearest relevant level");
    }

    [Fact]
    public void FarFromLevel_Warning_Added_Only_Once_Even_If_Multiple_TFs_Are_Far()
    {
        var farTf = DefaultSnapshot().M15 with
        {
            Trend = MarketTrend.Bullish,
            DistanceToSupport1Pct = 3.0m,
        };
        var snapshot = DefaultSnapshot() with
        {
            M15 = farTf,
            H1 = farTf with { Timeframe = "1h" },
        };
        var result = new List<string>();

        SnapshotHealthWarningsBuilder.AddFarFromLevelWarning(snapshot, AnalysisMode.Intraday, result);

        result.Should().ContainSingle(w => w == "price is far from nearest relevant level");
    }

    // ─── Truncation to MaxWarnings ───────────────────────────────────────────

    [Fact]
    public void Build_Truncates_Result_To_MaxWarnings_When_All_Rules_Fire()
    {
        // Настраиваем снапшот так, чтобы сработали все правила одновременно:
        // - near-staleness: OB, TF, Der = все в зоне близости
        // - low volume: VolumeRatio = 0.2
        // - conflicting: OB > 0, TF < 0
        // - directional + neutral: Bullish + Neutral
        // - far from level: distance > 1.5
        var lowFarTf = DefaultSnapshot().M15 with
        {
            VolumeRatio = 0.2m,
            Trend = MarketTrend.Bullish,
            DistanceToSupport1Pct = 2.5m,
        };
        var snapshot = ApiSnapshotTestData.CreateSnapshot(MarketTrend.Bullish) with
        {
            M15 = lowFarTf,
            H1 = lowFarTf with { Timeframe = "1h" },
            H4 = lowFarTf with { Timeframe = "4h" },
            Sentiment = DefaultSnapshot().Sentiment with
            {
                OrderBookPressureScore = 0.2m,
                TradeFlowPressureScore = -0.2m,
                MarketRegime = "Neutral",
            },
        };

        var ctx = BuildCtx(
            sectionAgesMs: new()
            {
                ["orderBook"] = 1800,  // 90% от 2000ms
                ["tradeFlow"] = 4500,  // 90% от 5000ms
                ["derivatives"] = 27000, // 90% от 30000ms
            });

        var result = SnapshotHealthWarningsBuilder.Build(snapshot, ctx);

        result.Count.Should().BeInRange(0, SnapshotHealthWarningsBuilder.MaxWarnings,
            because: $"warnings урезаются до {SnapshotHealthWarningsBuilder.MaxWarnings}");
    }

    [Fact]
    public void Build_Returns_Empty_When_No_Rules_Fire()
    {
        // Стандартный снапшот: volume = 1.1, MarketRegime = Trending,
        // distance = 0.6154, scores = aligned
        var snapshot = DefaultSnapshot();
        var ctx = BuildCtx(
            sectionAgesMs: new()
            {
                ["orderBook"] = 100,
                ["tradeFlow"] = 100,
                ["derivatives"] = 100,
            });

        var result = SnapshotHealthWarningsBuilder.Build(snapshot, ctx);

        result.Should().BeEmpty(because: "ни одно правило не должно срабатывать при нормальных данных");
    }

    [Fact]
    public void Build_Does_Not_Contain_Duplicate_Warnings()
    {
        // Один и тот же снапшот с двумя TF с низким объёмом — warning должен быть один
        var lowTf = DefaultSnapshot().M15 with { VolumeRatio = 0.1m };
        var snapshot = DefaultSnapshot() with
        {
            M15 = lowTf,
            H1 = lowTf with { Timeframe = "1h" },
            H4 = lowTf with { Timeframe = "4h" },
        };
        var ctx = BuildCtx();

        var result = SnapshotHealthWarningsBuilder.Build(snapshot, ctx);

        result.Should().OnlyHaveUniqueItems();
        result.Count(w => w == "low volume on primary timeframes").Should().Be(1);
    }
}
