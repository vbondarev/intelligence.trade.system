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
    public void Bullish_FarFromSupport_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 2.0m);

        result.Should().Be(EntryQuality.Poor,
            because: "dist > 1.50 → Poor");
    }

    [Fact]
    public void Bullish_NoSupport_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: null, distS: 0.5m);

        result.Should().Be(EntryQuality.Poor,
            because: "support1 == null → Poor");
    }

    [Fact]
    public void Bullish_ZeroDistance_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0m);

        result.Should().Be(EntryQuality.Poor,
            because: "distToSupport1 == 0 → Poor");
    }

    [Fact]
    public void Bullish_NullDistance_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: null);

        result.Should().Be(EntryQuality.Poor,
            because: "distToSupport1 == null → Poor");
    }

    [Fact]
    public void Bullish_Overbought_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.4m, overbought: true);

        result.Should().Be(EntryQuality.Poor,
            because: "rsiOverbought = true → Poor regardless of distance");
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
    public void Bearish_FarFromResistance_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 2.0m);

        result.Should().Be(EntryQuality.Poor,
            because: "dist > 1.50 → Poor");
    }

    [Fact]
    public void Bearish_NoResistance_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: null, distR: 0.3m);

        result.Should().Be(EntryQuality.Poor,
            because: "resistance1 == null → Poor");
    }

    [Fact]
    public void Bearish_Oversold_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m, oversold: true);

        result.Should().Be(EntryQuality.Poor,
            because: "rsiOversold = true → Poor regardless of distance");
    }

    [Fact]
    public void Bearish_ZeroDistance_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0m);

        result.Should().Be(EntryQuality.Poor,
            because: "distToResistance1 == 0 → Poor");
    }

    [Fact]
    public void Bearish_NullDistance_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: null);

        result.Should().Be(EntryQuality.Poor,
            because: "distToResistance1 == null → Poor");
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

    [Fact]
    public void Bullish_Confirmed_VeryLowVolume_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.19m);

        result.Should().Be(EntryQuality.Poor,
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
    public void Bearish_Confirmed_VeryLowVolume_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            volumeRatio: 0.19m);

        result.Should().Be(EntryQuality.Poor,
            because: "volumeRatio < 0.25 → Poor regardless of base quality");
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

    [Fact]
    public void Bullish_LowVolume_0_49_Returns_AtMostFair()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.49m);

        result.Should().NotBe(EntryQuality.Good,
            because: "volumeRatio=0.49 < 0.50 → cap Fair");
    }

    [Fact]
    public void Bullish_NormalVolume_0_70_Returns_Good()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            volumeRatio: 0.70m);

        result.Should().Be(EntryQuality.Good,
            because: "volumeRatio=0.70 ≥ 0.50 + all conditions OK → Good");
    }

    [Fact]
    public void Bearish_LowVolume_0_30_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            volumeRatio: 0.30m);

        result.Should().NotBe(EntryQuality.Good,
            because: "volumeRatio=0.30 < 0.50 → cap Fair");
    }

    [Fact]
    public void Bearish_LowVolume_0_49_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            volumeRatio: 0.49m);

        result.Should().NotBe(EntryQuality.Good,
            because: "volumeRatio=0.49 < 0.50 → cap Fair");
    }

    [Fact]
    public void Bearish_NormalVolume_0_70_Returns_Good()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            volumeRatio: 0.70m);

        result.Should().Be(EntryQuality.Good,
            because: "volumeRatio=0.70 ≥ 0.50 + all conditions OK → Good");
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

    // ─── Higher TF: near/far resistance/support ───────────────────────────────

    [Fact]
    public void Bullish_HigherTfResistanceFar_DoesNotRestrictGood()
    {
        // h4 resistance at 0.50% (≥ 0.30%) → no cap
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.50m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "higher TF resistance ≥ 0.30% → no opposite-level constraint → Good");
    }

    [Fact]
    public void Bullish_HigherTfResistanceNear_Returns_AtMostFair()
    {
        // m15 resistance null, h4 resistance at 0.20% → cap Fair
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.20m, oppStrength: 0.85m);

        result.Should().NotBe(EntryQuality.Good,
            because: "h4-like resistance 0.20% < 0.30% → Good forbidden");
    }

    [Fact]
    public void Bullish_HigherTfStrongResistanceVeryClose_Returns_Poor()
    {
        // m15 resistance null, h4 strong resistance at 0.10% → Poor
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0.5m,
            oppDistancePct: 0.10m, oppStrength: 0.80m);

        result.Should().Be(EntryQuality.Poor,
            because: "h4-like strong resistance 0.10% < 0.15% → Poor");
    }

    [Fact]
    public void Bearish_HigherTfSupportFar_DoesNotRestrictGood()
    {
        // h4 support at 0.50% → no cap
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.50m, oppStrength: 0.85m);

        result.Should().Be(EntryQuality.Good,
            because: "higher TF support ≥ 0.30% → no opposite-level constraint → Good");
    }

    [Fact]
    public void Bearish_HigherTfSupportNear_Returns_AtMostFair()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.20m, oppStrength: 0.85m);

        result.Should().NotBe(EntryQuality.Good,
            because: "h4-like support 0.20% < 0.30% → Good forbidden");
    }

    [Fact]
    public void Bearish_HigherTfStrongSupportVeryClose_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m,
            oppDistancePct: 0.10m, oppStrength: 0.80m);

        result.Should().Be(EntryQuality.Poor,
            because: "h4-like strong support 0.10% < 0.15% → Poor");
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

    /// <summary>
    /// V1: дистанция == 0 трактуется как «данные недоступны» и возвращает Poor.
    /// При ретесте уровня (dist ≈ 0) вызывающий код должен передавать
    /// небольшое ненулевое значение или null — это задокументированный контракт V1.
    /// </summary>
    [Fact]
    public void ZeroDistance_ReturnsPoor_PerCurrentV1Contract_Bullish()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0m);

        result.Should().Be(EntryQuality.Poor,
            because: "V1 contract: distancePct == 0 is treated as unavailable data → Poor; " +
                     "for exact level retests the caller should provide a small positive distance");
    }

    [Fact]
    public void ZeroDistance_ReturnsPoor_PerCurrentV1Contract_Bearish()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0m);

        result.Should().Be(EntryQuality.Poor,
            because: "V1 contract: distancePct == 0 is treated as unavailable data → Poor");
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

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
