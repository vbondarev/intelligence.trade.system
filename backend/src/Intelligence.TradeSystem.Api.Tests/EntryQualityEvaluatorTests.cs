using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;

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
        var result = EvaluateBullish(confirmed: true, support1: 0m, distS: 0.5m);

        result.Should().Be(EntryQuality.Poor,
            because: "support1 == 0 → Poor");
    }

    [Fact]
    public void Bullish_ZeroDistance_Returns_Poor()
    {
        var result = EvaluateBullish(confirmed: true, support1: 99m, distS: 0m);

        result.Should().Be(EntryQuality.Poor,
            because: "distToSupport1 == 0 → Poor");
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
        var result = EvaluateBearish(confirmed: true, resistance1: 0m, distR: 0.3m);

        result.Should().Be(EntryQuality.Poor,
            because: "resistance1 == 0 → Poor");
    }

    [Fact]
    public void Bearish_Oversold_Returns_Poor()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 110m, distR: 0.3m, oversold: true);

        result.Should().Be(EntryQuality.Poor,
            because: "rsiOversold = true → Poor regardless of distance");
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
    public void Good_Is_Impossible_When_Bullish_Support1_Is_Zero()
    {
        var result = EvaluateBullish(confirmed: true, support1: 0m, distS: 0.5m);

        result.Should().NotBe(EntryQuality.Good,
            because: "support1 == 0 → Poor; Good impossible without level");
    }

    [Fact]
    public void Good_Is_Impossible_When_Bearish_Resistance1_Is_Zero()
    {
        var result = EvaluateBearish(confirmed: true, resistance1: 0m, distR: 0.5m);

        result.Should().NotBe(EntryQuality.Good,
            because: "resistance1 == 0 → Poor; Good impossible without level");
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

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Bullish-ориентированный вызов: resistance1/distR/oversold фиксированы.</summary>
    private static EntryQuality EvaluateBullish(
        bool confirmed, decimal support1, decimal distS, bool overbought = false)
        => EntryQualityEvaluator.Evaluate(TimeframeBias.Bullish, confirmed,
            support1, distS, overbought,
            resistance1: 110m, distanceToResistance1Pct: 0.3m, rsiOversold: false);

    /// <summary>Bearish-ориентированный вызов: support1/distS/overbought фиксированы.</summary>
    private static EntryQuality EvaluateBearish(
        bool confirmed, decimal resistance1, decimal distR, bool oversold = false)
        => EntryQualityEvaluator.Evaluate(TimeframeBias.Bearish, confirmed,
            support1: 99m, distanceToSupport1Pct: 0.5m, rsiOverbought: false,
            resistance1, distR, oversold);

    /// <summary>Полный вызов со всеми параметрами.</summary>
    private static EntryQuality Evaluate(
        TimeframeBias bias, bool confirmed,
        decimal support1, decimal distS, bool overbought,
        decimal resistance1, decimal distR, bool oversold)
        => EntryQualityEvaluator.Evaluate(bias, confirmed,
            support1, distS, overbought,
            resistance1, distR, oversold);
}


