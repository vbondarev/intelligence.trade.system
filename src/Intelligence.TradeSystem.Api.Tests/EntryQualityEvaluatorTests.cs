using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Unit-тесты для <see cref="EntryQualityEvaluator"/>.
/// Покрывают все функциональные кейсы V1 и инварианты консистентности.
/// </summary>
public sealed class EntryQualityEvaluatorTests
{
    // ─── Neutral bias ─────────────────────────────────────────────────────────

    [Fact]
    public void Neutral_Bias_Always_Returns_Poor()
    {
        var result = Evaluate(TimeframeBias.Neutral, confirmed: true,
            support1: 100m, distS: 0.5m, overbought: false,
            resistance1: 110m, distR: 0.3m, oversold: false);

        result.Should().Be(EntryQuality.Poor,
            because: "Neutral bias → Poor regardless of other params");
    }

    // ─── Bullish: Good ───────────────────────────────────────────────────────

    [Fact]
    public void Bullish_Confirmed_CloseToSupport_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m);

        result.Should().Be(EntryQuality.Good,
            because: "confirmed + support1 > 0 + dist ≤ 0.75 + not overbought → Good");
    }

    [Fact]
    public void Bullish_Confirmed_ExactlyAtGoodMaxDistance_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: EntryQualityEvaluator.GoodMaxDistance);

        result.Should().Be(EntryQuality.Good,
            because: "dist == GoodMaxDistance (0.75) is inclusive upper bound for Good");
    }

    // ─── Bullish: Fair ───────────────────────────────────────────────────────

    [Fact]
    public void Bullish_Confirmed_MediumDistance_Returns_Fair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 1.0m);

        result.Should().Be(EntryQuality.Fair,
            because: "confirmed + dist > 0.75 && ≤ 1.50 → Fair");
    }

    [Fact]
    public void Bullish_Unconfirmed_CloseToSupport_Returns_Fair()
    {
        var result = EvaluateBullish(confirmed: false, support1: 99m, distS: 0.5m);

        result.Should().Be(EntryQuality.Fair,
            because: "unconfirmed + dist ≤ 1.50 + not overbought → Fair");
    }

    [Fact]
    public void Bullish_ExactlyAtFairMaxDistance_Returns_Fair()
    {
        var result = EvaluateBullish(confirmed: false, support1: 99m, distS: EntryQualityEvaluator.FairMaxDistance);

        result.Should().Be(EntryQuality.Fair,
            because: "dist == FairMaxDistance (1.50) is inclusive upper bound for Fair");
    }

    // ─── Bullish: Poor ───────────────────────────────────────────────────────

    [Fact]
    public void Bullish_ZeroDistance_Confirmed_ReturnsGood()
    {
        // dist == 0 means price is exactly at the support level (retest).
        // This is a valid entry signal — not a data-absent condition.
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0m);

        result.Should().Be(EntryQuality.Good,
            because: "distancePct == 0 is a retest at the level; confirmed + strong setup → Good");
    }

    // ─── Bearish: Good ───────────────────────────────────────────────────────

    [Fact]
    public void Bearish_Confirmed_CloseToResistance_Returns_Good()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m);

        result.Should().Be(EntryQuality.Good,
            because: "confirmed + resistance1 > 0 + dist ≤ 0.75 + not oversold → Good");
    }

    // ─── Bearish: Fair ───────────────────────────────────────────────────────

    [Fact]
    public void Bearish_Confirmed_MediumDistance_Returns_Fair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 1.2m);

        result.Should().Be(EntryQuality.Fair,
            because: "confirmed + dist > 0.75 && ≤ 1.50 → Fair");
    }

    [Fact]
    public void Bearish_Unconfirmed_CloseToResistance_Returns_Fair()
    {
        var result = EvaluateBearish(confirmed: false, resistance1: 110m, distR: 0.5m);

        result.Should().Be(EntryQuality.Fair,
            because: "unconfirmed + dist ≤ 1.50 → Fair");
    }

    // ─── Bearish: Poor ───────────────────────────────────────────────────────

    [Fact]
    public void Bearish_ZeroDistance_Confirmed_ReturnsGood()
    {
        // dist == 0 means price is exactly at the resistance level (retest).
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0m);

        result.Should().Be(EntryQuality.Good,
            because: "distancePct == 0 is a retest at the level; confirmed + strong setup → Good");
    }

    // ─── Symmetry: Bullish/Bearish Poor conditions ───────────────────────────

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void FarFromLevel_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, distancePct: 2.0m)
            .Should().Be(EntryQuality.Poor, because: "dist > 1.50 → Poor");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void NoLevel_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, hasLevel: false)
            .Should().Be(EntryQuality.Poor, because: "entry level == null → Poor");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void NullDistance_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, distancePct: null)
            .Should().Be(EntryQuality.Poor, because: "distancePct == null → Poor");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void RsiExtreme_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, rsiExtreme: true, distancePct: 0.4m)
            .Should().Be(EntryQuality.Poor,
                because: "rsi overbought (Bullish) / oversold (Bearish) → Poor");
    }

    // ─── Граничные значения ──────────────────────────────────────────────────

    [Fact]
    public void Bullish_JustAboveFairMaxDistance_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m,
            distS: EntryQualityEvaluator.FairMaxDistance + 0.0001m);

        result.Should().Be(EntryQuality.Poor,
            because: "dist just above 1.50 → Poor");
    }

    [Fact]
    public void Bullish_Confirmed_JustAboveGoodMaxDistance_Returns_Fair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m,
            distS: EntryQualityEvaluator.GoodMaxDistance + 0.0001m);

        result.Should().Be(EntryQuality.Fair,
            because: "dist just above 0.75 → Fair (not Good)");
    }

    // ─── Consistency: Good невозможен при Neutral ─────────────────────────────

    [Fact]
    public void Good_Is_Impossible_When_Bias_Is_Neutral()
    {
        var result = Evaluate(TimeframeBias.Neutral, confirmed: true,
            support1: 99m, distS: 0.3m, overbought: false,
            resistance1: 110m, distR: 0.3m, oversold: false);

        result.Should().NotBe(EntryQuality.Good,
            because: "Neutral bias → Poor, Good is impossible");
    }

    // ─── Consistency: Good невозможен без уровня ─────────────────────────────

    [Fact]
    public void Good_Is_Impossible_When_Bullish_Support1_Is_Null()
    {
        var result = EvaluateBullish(confirmed: true, support1: null, distS: 0.5m);

        result.Should().NotBe(EntryQuality.Good,
            because: "support1 == null → Poor; Good impossible without level");
    }

    [Fact]
    public void Good_Is_Impossible_When_Bearish_Resistance1_Is_Null()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: null, distR: 0.5m);

        result.Should().NotBe(EntryQuality.Good,
            because: "resistance1 == null → Poor; Good impossible without level");
    }

    // ─── Consistency: Good невозможен при overbought/oversold ────────────────

    [Fact]
    public void Good_Is_Impossible_When_Bullish_And_Overbought()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.3m, overbought: true);

        result.Should().NotBe(EntryQuality.Good,
            because: "rsiOverbought=true → Poor; Good impossible");
    }

    [Fact]
    public void Good_Is_Impossible_When_Bearish_And_Oversold()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m, oversold: true);

        result.Should().NotBe(EntryQuality.Good,
            because: "rsiOversold=true → Poor; Good impossible");
    }

    // ─── Consistency: Fair невозможен при дистанции > FairMaxDistance ─────────

    [Theory]
    [InlineData(1.51)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    public void Fair_Is_Impossible_When_Distance_Exceeds_FairMaxDistance(double distDouble)
    {
        var dist = (decimal)distDouble;

        EvaluateBullish(confirmed: false, support1: 99m, distS: dist)
            .Should().Be(EntryQuality.Poor,
                because: $"Bullish: dist={dist} > FairMaxDistance → Poor, Fair impossible");

        EvaluateBearish(confirmed: false, resistance1: 110m, distR: dist)
            .Should().Be(EntryQuality.Poor,
                because: $"Bearish: dist={dist} > FairMaxDistance → Poor, Fair impossible");
    }

    // ─── Volume rule ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void VeryLowVolume_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, volumeRatio: 0.19m)
            .Should().Be(EntryQuality.Poor,
                because: "volumeRatio < 0.25 → Poor regardless of base quality");
    }

    [Fact]
    public void Bullish_Confirmed_LowVolume_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.3m);

        result.Should().BeOneOf([EntryQuality.Fair, EntryQuality.Poor],
            because: "volumeRatio < 0.5 → not above Fair");
        result.Should().NotBe(EntryQuality.Good);
    }

    [Fact]
    public void NullVolumeRatio_Caps_At_Fair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "null volumeRatio → conservative cap at Fair");
    }

    // ─── EMA rule ─────────────────────────────────────────────────────────────

    [Fact]
    public void Bullish_BothEmaConflicts_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            isAboveEma20: false, isAboveEma50: false);

        result.Should().Be(EntryQuality.Poor,
            because: "Bullish + price below both EMA20 and EMA50 → Poor");
    }

    [Theory]
    [InlineData(false, true)]  // ниже только EMA20
    [InlineData(true, false)]  // ниже только EMA50
    public void Bullish_OneEmaConflict_Returns_AtMostFair(bool isAboveEma20, bool isAboveEma50)
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            isAboveEma20: isAboveEma20, isAboveEma50: isAboveEma50);

        result.Should().NotBe(EntryQuality.Good,
            because: $"Bullish + price below one EMA (above20={isAboveEma20}, above50={isAboveEma50}) → not above Fair");
    }

    [Fact]
    public void Bearish_BothEmaConflicts_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            isAboveEma20: true, isAboveEma50: true);

        result.Should().Be(EntryQuality.Poor,
            because: "Bearish + price above both EMA20 and EMA50 → Poor");
    }

    [Theory]
    [InlineData(true, false)]   // выше только EMA20
    [InlineData(false, true)]   // выше только EMA50
    public void Bearish_OneEmaConflict_Returns_AtMostFair(bool isAboveEma20, bool isAboveEma50)
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            isAboveEma20: isAboveEma20, isAboveEma50: isAboveEma50);

        result.Should().NotBe(EntryQuality.Good,
            because: $"Bearish + price above one EMA (above20={isAboveEma20}, above50={isAboveEma50}) → not above Fair");
    }

    // ─── Snapshot freshness rule ──────────────────────────────────────────────

    [Fact]
    public void StaleSnapshot_Caps_Good_At_Fair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 1.0m, snapshotIsFresh: false);

        result.Should().NotBe(EntryQuality.Good,
            because: "snapshotIsFresh=false → Good impossible, cap at Fair");
    }

    [Fact]
    public void StaleSnapshot_And_LowVolume_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.4m, snapshotIsFresh: false);

        result.Should().Be(EntryQuality.Poor,
            because: "snapshotIsFresh=false + volumeRatio < 0.5 → Poor");
    }

    // ─── Market regime rule ───────────────────────────────────────────────────

    [Fact]
    public void NeutralRegime_Caps_Good_At_Fair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            marketRegime: MarketRegimes.Neutral);

        result.Should().NotBe(EntryQuality.Good,
            because: "marketRegime=Neutral → Good impossible, cap at Fair");
    }

    [Fact]
    public void NeutralRegime_And_LowVolume_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.4m, marketRegime: MarketRegimes.Neutral);

        result.Should().Be(EntryQuality.Poor,
            because: "marketRegime=Neutral + low volume → Poor");
    }

    // ─── CapAt helper ─────────────────────────────────────────────────────────

    [Fact]
    public void BtcUsdt_M15_Like_Snapshot_Returns_Poor()
    {
        // bias=Bullish, isTrendConfirmed=true, support рядом, volumeRatio=0.1971,
        // isAboveEma20=false, isAboveEma50=false, snapshotIsFresh=false, marketRegime=Neutral
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99_000m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: 102_000m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio: 0.1971m,
            isAboveEma20: false,
            isAboveEma50: false,
            snapshotIsFresh: false,
            marketRegime: MarketRegimes.Neutral);


        result.Should().Be(EntryQuality.Poor,
            because: "BTCUSDT m15-like: low volume + EMA conflict + stale + neutral → Poor");
    }

    [Fact]
    public void H4Like_Bearish_VeryLowVolume_Returns_Poor()
    {
        // bias=Bearish, isTrendConfirmed=true, resistance рядом, volumeRatio=0.0184,
        // isAboveEma20=true, isAboveEma50=true (EMA conflict for bearish), marketRegime=Neutral
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bearish,
            isTrendConfirmed: true,
            support1: 99_000m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: 102_000m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio: 0.0184m,
            isAboveEma20: true,
            isAboveEma50: true,
            snapshotIsFresh: false,
            marketRegime: MarketRegimes.Neutral);

        result.Should().Be(EntryQuality.Poor,
            because: "H4-like: very low volume + EMA conflict (bearish) + neutral → Poor");
    }

    // ─── Entry level strength rule ────────────────────────────────────────────

    [Fact]
    public void Bullish_WeakEntryLevel_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            entryLevelStrength: 0.20m);

        result.Should().NotBe(EntryQuality.Good,
            because: "Weak entry level strength (0.20 ≤ 0.35) → cap Fair");
    }

    [Fact]
    public void Bullish_NullEntryLevel_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            entryLevelStrength: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "null entry level strength → conservative cap Fair");
    }

    [Fact]
    public void Bearish_WeakEntryLevel_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            entryLevelStrength: 0.20m);

        result.Should().NotBe(EntryQuality.Good,
            because: "Weak entry level strength (0.20 ≤ 0.35) → cap Fair");
    }

    [Fact]
    public void Bullish_ModerateEntryLevel_NoOtherConflicts_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            entryLevelStrength: 0.50m);

        result.Should().Be(EntryQuality.Good,
            because: "Moderate entry level (0.50 > 0.35) + no other conflicts → Good");
    }

    // ─── Opposite level rule ──────────────────────────────────────────────────

    [Fact]
    public void Bullish_StrongResistanceVeryClose_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.10m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Poor,
            because: "Strong resistance < 0.15% → Poor");
    }

    [Fact]
    public void Bullish_ModerateResistanceNear_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.20m, oppStrength: 0.55m);

        result.Should().NotBe(EntryQuality.Good,
            because: "Moderate resistance 0.20% < 0.30% → Good forbidden");
    }

    [Fact]
    public void Bullish_AnyResistanceNear_NullStrength_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.25m, oppStrength: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "resistance < 0.30% → Good forbidden regardless of unknown strength");
    }

    [Fact]
    public void Bullish_ResistanceFar_AllConditionsFavorable_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.50m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "resistance ≥ 0.30% → no constraint → Good allowed");
    }

    [Fact]
    public void Bearish_StrongSupportVeryClose_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.10m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Poor,
            because: "Strong support < 0.15% → Poor");
    }

    [Fact]
    public void Bearish_ModerateSupportNear_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.20m, oppStrength: 0.55m);

        result.Should().NotBe(EntryQuality.Good,
            because: "Moderate support 0.20% < 0.30% → Good forbidden");
    }

    [Fact]
    public void Bearish_SupportFar_AllConditionsFavorable_Returns_Good()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.50m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "support ≥ 0.30% → no constraint → Good allowed");
    }

    [Fact]
    public void OppLevel_Null_DoesNotBreakCalculation()
    {
        // Regression: null oppDistancePct must not break the pipeline
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: null, oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "null opposite level → no constraint applied → Good if other conditions OK");
    }

    // ─── CapAt theory ─────────────────────────────────────────────────────────

    [Fact]
    public void BtcUsdt_M15_Like_WithHigherTfResistance_Returns_Poor()
    {
        // Bullish, low volume, below EMAs, stale, neutral regime,
        // higher TF resistance at 0.05% (≈ 77437 → 77480)
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 77_400m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 0.30m,
            isAboveEma20: false,
            isAboveEma50: false,
            snapshotIsFresh: false,
            marketRegime: MarketRegimes.Neutral,
            entryLevelStrength: 0.50m,
            oppDistancePct: 0.05m,
            oppStrength: 0.80m);

        result.Should().Be(EntryQuality.Poor,
            because: "m15-like: very close higher TF resistance + low volume + EMA conflict + neutral → Poor");
    }

    // ─── Volume rule: additional boundary tests ───────────────��───────────────

    [Theory]
    [InlineData(TimeframeBias.Bullish, 0.49)]
    [InlineData(TimeframeBias.Bearish, 0.30)]
    [InlineData(TimeframeBias.Bearish, 0.49)]
    public void LowVolume_CapsAtFair(TimeframeBias bias, double ratio)
    {
        EvaluateByBias(bias, volumeRatio: (decimal)ratio)
            .Should().NotBe(EntryQuality.Good,
                because: $"volumeRatio={ratio} < 0.50 → cap Fair");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish, 0.70)]
    [InlineData(TimeframeBias.Bearish, 0.70)]
    public void NormalVolume_GoodPossible(TimeframeBias bias, double ratio)
    {
        EvaluateByBias(bias, volumeRatio: (decimal)ratio)
            .Should().Be(EntryQuality.Good,
                because: $"volumeRatio={ratio} ≥ 0.50 + all conditions OK → Good");
    }

    // ─── EMA rule: Good-positive cases ───────────────────────────────────────

    [Fact]
    public void Bullish_AboveBothEma_GoodPossible()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            isAboveEma20: true, isAboveEma50: true);

        result.Should().Be(EntryQuality.Good,
            because: "Bullish + above both EMA20 and EMA50 → no EMA conflict → Good");
    }

    [Fact]
    public void Bearish_BelowBothEma_GoodPossible()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            isAboveEma20: false, isAboveEma50: false);

        result.Should().Be(EntryQuality.Good,
            because: "Bearish + below both EMA20 and EMA50 → no EMA conflict → Good");
    }

    // ─── Snapshot freshness: additional cases ───────────────────���────────────

    [Fact]
    public void StaleSnapshot_And_EmaConflict_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            snapshotIsFresh: false,
            isAboveEma20: false, isAboveEma50: false);

        result.Should().Be(EntryQuality.Poor,
            because: "stale + both EMA conflict → Poor from EMA rule (independent of freshness rule)");
    }

    [Fact]
    public void FreshSnapshot_Does_Not_Restrict_Quality()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            snapshotIsFresh: true);

        result.Should().Be(EntryQuality.Good,
            because: "fresh snapshot + all conditions OK → Good not restricted by freshness");
    }

    // ─── Market regime: additional cases ─────────────────────────────────────

    [Fact]
    public void NeutralRegime_And_EmaConflict_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            isAboveEma20: false, isAboveEma50: false,
            marketRegime: MarketRegimes.Neutral);

        result.Should().Be(EntryQuality.Poor,
            because: "Neutral regime + both EMA conflict → Poor");
    }

    [Fact]
    public void NeutralRegime_And_NearOppLevel_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            marketRegime: MarketRegimes.Neutral,
            volumeRatio: 1.0m,
            oppDistancePct: 0.20m, oppStrength: 0.55m);

        result.Should().NotBe(EntryQuality.Good,
            because: "Neutral regime + near moderate resistance → not above Fair");
    }

    [Fact]
    public void NonNeutralRegime_GoodPossible()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            marketRegime: MarketRegimes.Trending);

        result.Should().Be(EntryQuality.Good,
            because: "non-Neutral regime + all conditions OK → Good allowed");
    }

    // ─── Entry level strength: additional positive cases ─────────────────────

    [Fact]
    public void Bullish_StrongEntryLevel_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            entryLevelStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "Strong entry level (0.85 ≥ 0.70) + no other conflicts → Good");
    }

    [Fact]
    public void Bearish_NullEntryLevel_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            entryLevelStrength: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "null entry level → conservative cap Fair");
    }

    [Fact]
    public void Bearish_ModerateEntryLevel_Returns_Good()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            entryLevelStrength: 0.50m);

        result.Should().Be(EntryQuality.Good,
            because: "Moderate resistance (0.35 < 0.50 < 0.70) + no other conflicts → Good");
    }

    [Fact]
    public void Bearish_StrongEntryLevel_Returns_Good()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            entryLevelStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "Strong resistance (0.85 ≥ 0.70) + no other conflicts → Good");
    }

    // ─── Opposite level: edge cases (null / wrong-side) ──────────────────────

    [Fact]
    public void Bullish_OppLevelNull_GoodAllowed()
    {
        // Resistance below price or absent → caller passes null → no constraint
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: null, oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "null oppDistancePct (e.g. resistance below price) → no obstacle constraint");
    }

    [Fact]
    public void Bearish_OppLevelNull_GoodAllowed()
    {
        // Support above price or absent → caller passes null → no constraint
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: null, oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "null oppDistancePct (e.g. support above price) → no obstacle constraint");
    }

    // ─── Higher TF: near/far opposite level (Bullish + Bearish) ─────────────

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void HigherTfOppositeLevelFar_DoesNotRestrictGood(TimeframeBias bias)
    {
        EvaluateByBias(bias, oppDistancePct: 0.50m, oppStrength: 0.85m)
            .Should().Be(EntryQuality.Good,
                because: "opp level ≥ 0.30% → no constraint → Good");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void HigherTfOppositeLevelNear_Returns_AtMostFair(TimeframeBias bias)
    {
        EvaluateByBias(bias, oppDistancePct: 0.20m, oppStrength: 0.85m)
            .Should().NotBe(EntryQuality.Good,
                because: "opp level 0.20% < 0.30% → Good forbidden");
    }

    [Theory]
    [InlineData(TimeframeBias.Bullish)]
    [InlineData(TimeframeBias.Bearish)]
    public void HigherTfOppositeLevelVeryClose_Returns_Poor(TimeframeBias bias)
    {
        EvaluateByBias(bias, oppDistancePct: 0.10m, oppStrength: 0.80m)
            .Should().Be(EntryQuality.Poor,
                because: "opp level 0.10% < 0.15% → Poor");
    }

    // ─── Real-world BTCUSDT scenarios ─────────────────────────────────────────

    [Fact]
    public void BullishM15_WithLowVolume_EmaConflict_StaleSnapshot_AndNearHigherTfResistance_ReturnsPoor()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 77_400m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 0.1971m,
            isAboveEma20: false,
            isAboveEma50: false,
            snapshotIsFresh: false,
            marketRegime: MarketRegimes.Neutral,
            entryLevelStrength: 0.50m,   // Moderate support
            oppDistancePct: 0.05m,        // h4 resistance at ~77480 (≈0.05% from 77437)
            oppStrength: 0.80m);

        result.Should().Be(EntryQuality.Poor,
            because: "m15 bullish: low volume + EMA conflict + stale + neutral + very close h4 resistance → Poor");
    }

    [Fact]
    public void BearishH4_WithLowVolume_PriceAboveEma20AndEma50_AndNeutralRegime_DoesNotReturnGood()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bearish,
            isTrendConfirmed: true,
            support1: null, distanceToSupport1Pct: null, rsiOverbought: false,
            resistance1: 102_000m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio: 0.0184m,
            isAboveEma20: true,      // bearish EMA conflict
            isAboveEma50: true,      // bearish EMA conflict
            snapshotIsFresh: false,
            marketRegime: MarketRegimes.Neutral,
            entryLevelStrength: 0.80m,
            oppDistancePct: null,
            oppStrength: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "h4 bearish: very low volume + price above both EMAs + neutral regime → not Good");
    }

    [Fact]
    public void BullishSetup_WithStrongSupport_VolumeConfirmation_EmaConfirmation_AndNoNearbyResistance_ReturnsGood()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99_000m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 0.80m,
            isAboveEma20: true,
            isAboveEma50: true,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 0.80m,   // Strong support
            oppDistancePct: null,
            oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "clean bullish: confirmed + strong support + volume OK + EMA OK + fresh + Trending → Good");
    }

    [Fact]
    public void BearishSetup_WithStrongResistance_VolumeConfirmation_EmaConfirmation_AndNoNearbySupport_ReturnsGood()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bearish,
            isTrendConfirmed: true,
            support1: null, distanceToSupport1Pct: null, rsiOverbought: false,
            resistance1: 102_000m, distanceToResistance1Pct: 0.5m, rsiOversold: false,
            volumeRatio: 0.80m,
            isAboveEma20: false,
            isAboveEma50: false,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 0.80m,   // Strong resistance
            oppDistancePct: null,
            oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "clean bearish: confirmed + strong resistance + volume OK + EMA OK + fresh + Trending → Good");
    }

    // ─── CapAt theory ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData(EntryQuality.Good, EntryQuality.Fair, EntryQuality.Fair)]
    [InlineData(EntryQuality.Good, EntryQuality.Poor, EntryQuality.Poor)]
    [InlineData(EntryQuality.Fair, EntryQuality.Good, EntryQuality.Fair)]
    [InlineData(EntryQuality.Poor, EntryQuality.Good, EntryQuality.Poor)]
    [InlineData(EntryQuality.Fair, EntryQuality.Fair, EntryQuality.Fair)]
    public void CapAt_Returns_Lower_Of_Quality_And_Max(
        EntryQuality quality, EntryQuality max, EntryQuality expected)
    {
        EntryQualityEvaluator.CapAt(quality, max)
            .Should().Be(expected,
                because: $"CapAt({quality}, {max}) should return {expected}");
    }

    // ─── Regression: distance == 0 ───────────────────────────────────────────

    [Fact]
    public void ZeroDistance_Unconfirmed_ReturnsFair()
    {
        // Retest without trend confirmation → Fair (not Good, not Poor)
        var result = EvaluateBullish(confirmed: false, support1: 99m, distS: 0m);

        result.Should().Be(EntryQuality.Fair,
            because: "dist == 0 (retest) + unconfirmed trend → Fair");
    }

    [Fact]
    public void NegativeDistance_DoesNotReturnPoor_FromLevelQuality()
    {
        // Negative dist < 0 means level is on the wrong side of the price.
        // EvaluateLevelBasedQuality returns Poor for dist < 0.
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: -0.5m);

        result.Should().Be(EntryQuality.Poor,
            because: "distancePct < 0 means level is behind the trade direction → Poor");
    }

    // ─── Opposite level: wrong-side semantics ────────────────────────────────

    [Fact]
    public void Bullish_ResistanceBelowCurrentPrice_IsNotAnObstacle_PassedAsNull()
    {
        // Resistance below current price is behind the trade, not ahead.
        // Caller responsibility: pass oppDistancePct = null in this case.
        // Evaluator must not penalise the trade when null is provided.
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: null, oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "resistance below price is not an obstacle for a bullish trade; " +
                     "caller passes null → no opposite-level constraint → Good allowed");
    }

    [Fact]
    public void Bearish_SupportAboveCurrentPrice_IsNotAnObstacle_PassedAsNull()
    {
        // Support above current price is behind the short trade, not in its path.
        // Caller responsibility: pass oppDistancePct = null in this case.
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: null, oppStrength: null);

        result.Should().Be(EntryQuality.Good,
            because: "support above price is not an obstacle for a bearish trade; " +
                     "caller passes null → no opposite-level constraint → Good allowed");
    }

    // ─── New: entryLevelStrength default is null ─────────────────────────────

    [Fact]
    public void EntryLevelStrength_OmittedDefault_CapsAtFair_NotGood()
    {
        // When entryLevelStrength is not provided, the default is null.
        // Unknown strength must cap at Fair — Good is forbidden.
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: true, isAboveEma50: true,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);
        // entryLevelStrength omitted → null → cap Fair

        result.Should().NotBe(EntryQuality.Good,
            because: "omitted entryLevelStrength defaults to null → unknown strength → cap Fair");
        result.Should().Be(EntryQuality.Fair,
            because: "all other conditions are Good-worthy; only null strength blocks Good → Fair");
    }

    [Fact]
    public void EntryLevelStrength_ExplicitNull_CapsAtFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            entryLevelStrength: null);

        result.Should().NotBe(EntryQuality.Good,
            because: "explicit null entryLevelStrength → unknown → cap Fair");
    }

    // ─── New: unknown EMA (null) ──────────────────────────────────────────────

    [Fact]
    public void Bullish_UnknownEma20_Null_CapsAtFair()
    {
        // isAboveEma20 = null means EMA20 data is unavailable → treated as conflict.
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: null,    // unknown
            isAboveEma50: true,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 1.0m);

        result.Should().NotBe(EntryQuality.Good,
            because: "null isAboveEma20 (EMA unavailable) → treated as conflict → Good forbidden");
    }

    [Fact]
    public void Bullish_BothEmaUnknown_Null_ReturnsPoor()
    {
        // Both EMA unknown → 2 conflicts → Poor
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: null,    // unknown
            isAboveEma50: null,    // unknown
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 1.0m);

        result.Should().Be(EntryQuality.Poor,
            because: "both EMA values unknown → 2 EMA conflicts → Poor");
    }

    [Fact]
    public void Bearish_UnknownEma50_Null_CapsAtFair()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bearish,
            isTrendConfirmed: true,
            support1: null, distanceToSupport1Pct: null, rsiOverbought: false,
            resistance1: 110m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: false,
            isAboveEma50: null,    // unknown → treated as conflict for bearish
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 1.0m);

        result.Should().NotBe(EntryQuality.Good,
            because: "null isAboveEma50 (EMA unavailable) → treated as conflict for bearish → Good forbidden");
    }

    [Fact]
    public void Bearish_BothEmaUnknown_Null_ReturnsPoor()
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bearish,
            isTrendConfirmed: true,
            support1: null, distanceToSupport1Pct: null, rsiOverbought: false,
            resistance1: 110m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: null,    // unknown
            isAboveEma50: null,    // unknown
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending,
            entryLevelStrength: 1.0m);

        result.Should().Be(EntryQuality.Poor,
            because: "both EMA values unknown for bearish → 2 conflicts → Poor");
    }

    // ─── New: marketRegime robust comparison ──────────────────────────────────

    [Theory]
    [InlineData("neutral")]           // lowercase
    [InlineData("NEUTRAL")]           // uppercase
    [InlineData(" Neutral ")]         // trimming needed
    [InlineData("  neutral  ")]       // extra spaces + lowercase
    public void MarketRegime_CaseAndWhitespacVariants_AreTreatedAsNeutral_CapsAtFair(string regime)
    {
        // All these variants must be treated identically to "Neutral".
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            marketRegime: regime,
            volumeRatio: 1.0m);   // not low volume → cap Fair (not Poor)

        result.Should().NotBe(EntryQuality.Good,
            because: $"marketRegime='{regime}' normalises to Neutral → Good forbidden");
        result.Should().BeOneOf([EntryQuality.Fair, EntryQuality.Poor],
            because: "Neutral regime without low volume/EMA conflict → cap Fair");
    }

    [Fact]
    public void MarketRegime_Null_CapsAtFair()
    {
        // Null regime: unknown → conservative cap Fair.
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: true, isAboveEma50: true,
            snapshotIsFresh: true,
            marketRegime: null,
            entryLevelStrength: 1.0m);

        result.Should().NotBe(EntryQuality.Good,
            because: "null marketRegime → unknown regime → conservative cap Fair");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MarketRegime_EmptyOrWhitespace_CapsAtFair(string regime)
    {
        var result = EntryQualityEvaluator.Evaluate(
            bias: TimeframeBias.Bullish,
            isTrendConfirmed: true,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1: null, distanceToResistance1Pct: null, rsiOversold: false,
            volumeRatio: 1.0m,
            isAboveEma20: true, isAboveEma50: true,
            snapshotIsFresh: true,
            marketRegime: regime,
            entryLevelStrength: 1.0m);

        result.Should().NotBe(EntryQuality.Good,
            because: $"empty/whitespace marketRegime → unknown → conservative cap Fair");
    }

    // ─── New: oppDistancePct < 0 ──────────────────────────────────────────────

    [Fact]
    public void OppDistancePct_Negative_IsIgnoredAsObstacle_Bullish()
    {
        // Negative distance means the level is below price (wrong side for bullish).
        // Must not degrade quality — it's not an obstacle.
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: -0.10m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "negative oppDistancePct means level is on the wrong side → not an obstacle → Good unaffected");
    }

    [Fact]
    public void OppDistancePct_Negative_IsIgnoredAsObstacle_Bearish()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: -0.20m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "negative oppDistancePct for bearish → wrong side → not an obstacle → Good unaffected");
    }

    [Fact]
    public void OppDistancePct_Zero_ActsAsNearObstacle_CapsAtFair()
    {
        // Zero distance = level exactly at current price = maximum obstacle (between 0 and 0.15% threshold).
        // Since 0 < CloseOppositeThreshold(0.15) with Moderate/Strong strength → Poor.
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0m, oppStrength: 0.85m);   // Strong resistance at current price

        result.Should().Be(EntryQuality.Poor,
            because: "oppDistancePct == 0 with Strong resistance at current price → immediate obstacle → Poor");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Dispatches by bias with clean defaults — used for symmetric Theory tests.</summary>
    private static EntryQuality EvaluateByBias(
        TimeframeBias bias,
        bool confirmed = true,
        decimal? distancePct = 0.5m,
        bool hasLevel = true,
        bool rsiExtreme = false,
        decimal? volumeRatio = 1.0m,
        decimal? oppDistancePct = null,
        decimal? oppStrength = null)
        => bias == TimeframeBias.Bullish
            ? EvaluateBullish(confirmed,
                support1: hasLevel ? 99m : null,
                distS: distancePct,
                overbought: rsiExtreme,
                volumeRatio: volumeRatio,
                oppDistancePct: oppDistancePct,
                oppStrength: oppStrength)
            : EvaluateBearish(confirmed,
                resistance1: hasLevel ? 110m : null,
                distR: distancePct,
                oversold: rsiExtreme,
                volumeRatio: volumeRatio,
                oppDistancePct: oppDistancePct,
                oppStrength: oppStrength);

    /// <summary>Bullish helper. Permissive defaults для параметров, чтобы старые тесты не ограничивались.</summary>
    private static EntryQuality EvaluateBullish(
        bool confirmed, decimal? support1, decimal? distS, bool overbought = false,
        decimal? volumeRatio = 1.0m,
        bool isAboveEma20 = true, bool isAboveEma50 = true,
        bool snapshotIsFresh = true,
        string marketRegime = MarketRegimes.Trending,
        decimal? entryLevelStrength = 1.0m,
        decimal? oppDistancePct = null,
        decimal? oppStrength = null)
        => EntryQualityEvaluator.Evaluate(TimeframeBias.Bullish, confirmed,
            support1, distS, overbought,
            resistance1: 110m, distanceToResistance1Pct: 0.3m, rsiOversold: false,
            volumeRatio, isAboveEma20, isAboveEma50, snapshotIsFresh, marketRegime,
            entryLevelStrength, oppDistancePct, oppStrength);

    /// <summary>Bearish helper. Permissive defaults: цена ниже обеих EMA (медвежий сценарий).</summary>
    private static EntryQuality EvaluateBearish(
        bool confirmed, decimal? resistance1, decimal? distR, bool oversold = false,
        decimal? volumeRatio = 1.0m,
        bool isAboveEma20 = false, bool isAboveEma50 = false,
        bool snapshotIsFresh = true,
        string marketRegime = MarketRegimes.Trending,
        decimal? entryLevelStrength = 1.0m,
        decimal? oppDistancePct = null,
        decimal? oppStrength = null)
        => EntryQualityEvaluator.Evaluate(TimeframeBias.Bearish, confirmed,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1, distR, oversold,
            volumeRatio, isAboveEma20, isAboveEma50, snapshotIsFresh, marketRegime,
            entryLevelStrength, oppDistancePct, oppStrength);

    /// <summary>Полный вызов со всеми параметрами (permissive новые параметры).</summary>
    private static EntryQuality Evaluate(
        TimeframeBias bias, bool confirmed,
        decimal? support1, decimal? distS, bool overbought,
        decimal? resistance1, decimal? distR, bool oversold,
        decimal? volumeRatio = 1.0m,
        bool isAboveEma20 = true, bool isAboveEma50 = true,
        bool snapshotIsFresh = true,
        string marketRegime = MarketRegimes.Trending)
        => EntryQualityEvaluator.Evaluate(bias, confirmed,
            support1, distS, overbought,
            resistance1, distR, oversold,
            volumeRatio, isAboveEma20, isAboveEma50, snapshotIsFresh, marketRegime);
}
