using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Models.Payloads;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Tests;

/// <summary>
/// Прямые unit-тесты для <c>LlmTimeframeSummaryBuilder</c>.
/// Работают без HTTP-стека — значительно быстрее endpoint-тестов.
/// Покрывают инварианты согласованности summary-полей.
/// </summary>
public sealed class LlmTimeframeSummaryBuilderTests
{
    // ─── Bullish fully confirmed ─────────────────────────────────────────────

    [Fact]
    public void Build_BullishFullyConfirmed_AllSummaryFieldsAreConsistent()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m);

        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.TrendStrengthLabel.Should().Be(TrendStrengthLabel.Strong);
        r.Bias.Should().Be(TimeframeBias.Bullish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.MomentumState.Should().Be(MomentumState.Healthy);
        r.EntryQuality.Should().NotBe(EntryQuality.Poor, because: "confirmed bullish with healthy RSI should not be Poor");
        r.RiskFlags.Should().NotContain("WeakTrend");
    }

    // ─── Bearish fully confirmed ─────────────────────────────────────────────

    [Fact]
    public void Build_BearishFullyConfirmed_AllSummaryFieldsAreConsistent()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            rsi14: 38m, trendStrengthScore: 0.85m);

        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.TrendStrengthLabel.Should().Be(TrendStrengthLabel.Strong);
        r.Bias.Should().Be(TimeframeBias.Bearish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.MomentumState.Should().Be(MomentumState.Healthy);
        r.EntryQuality.Should().NotBe(EntryQuality.Poor);
        r.RiskFlags.Should().NotContain("WeakTrend");
    }

    // ─── Sideways ────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Sideways_BiasIsNeutralAndConfirmedFalse()
    {
        var s = MakeSnapshot(trend: MarketTrend.Sideways, rsi14: 50m, trendStrengthScore: 0.3m);

        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.IsTrendConfirmed.Should().BeFalse();
        r.MomentumState.Should().Be(MomentumState.Neutral);
        r.EntryQuality.Should().Be(EntryQuality.Poor);
    }

    // ─── Unknown ─────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Unknown_TrendStrengthLabelIsUndefinedAndConfirmedFalse()
    {
        var s = MakeSnapshot(trend: MarketTrend.Unknown, rsi14: 50m, trendStrengthScore: 0.9m);

        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.TrendStrengthLabel.Should().Be(TrendStrengthLabel.Undefined,
            because: "Unknown trend always yields Undefined label regardless of score");
        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.IsTrendConfirmed.Should().BeFalse();
        r.MomentumState.Should().Be(MomentumState.Neutral);
    }

    // ─── Invariant: Bias=Neutral ⇒ IsTrendConfirmed=false ───────────────────

    [Theory]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public void Build_NeutralBias_IsTrendConfirmedAlwaysFalse(MarketTrend trend)
    {
        var s = MakeSnapshot(trend: trend);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.IsTrendConfirmed.Should().BeFalse(
            because: "Neutral bias cannot produce confirmed trend");
    }

    // ─── Invariant: IsTrendConfirmed=true ⇒ Bias≠Neutral ────────────────────

    [Fact]
    public void Build_WhenIsTrendConfirmedTrue_BiasIsNotNeutral()
    {
        // Bullish confirmed
        var bullish = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true);
        var rb = LlmTimeframeSummaryBuilder.Build(bullish);
        if (rb.IsTrendConfirmed)
            rb.Bias.Should().NotBe(TimeframeBias.Neutral);

        // Bearish confirmed
        var bearish = MakeSnapshot(trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false);
        var rr = LlmTimeframeSummaryBuilder.Build(bearish);
        if (rr.IsTrendConfirmed)
            rr.Bias.Should().NotBe(TimeframeBias.Neutral);
    }

    // ─── Invariant: Healthy ⇒ IsTrendConfirmed=true ──────────────────────────

    [Fact]
    public void Build_WhenMomentumIsHealthy_IsTrendConfirmedIsTrue()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true, rsi14: 60m);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        if (r.MomentumState == MomentumState.Healthy)
            r.IsTrendConfirmed.Should().BeTrue(because: "Healthy momentum requires confirmed trend");
    }

    // ─── Invariant: Sideways/Unknown ⇒ Bias=Neutral ─────────────────────────

    [Theory]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public void Build_SidewaysOrUnknown_BiasIsNeutral(MarketTrend trend)
    {
        // Even with EMA flags set to true, Sideways/Unknown must yield Neutral bias.
        var s = MakeSnapshot(trend: trend, emaBullish: true, emaBearish: true, isAboveEma200: true);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.Bias.Should().Be(TimeframeBias.Neutral,
            because: $"{trend} must always produce Neutral bias regardless of EMA flags");
    }

    // ─── Invariant: Unknown ⇒ TrendStrengthLabel=Undefined ──────────────────

    [Fact]
    public void Build_Unknown_TrendStrengthLabelIsUndefined_RegardlessOfScore()
    {
        foreach (var score in new[] { 0m, 0.5m, 0.8m, 1.0m })
        {
            var s = MakeSnapshot(trend: MarketTrend.Unknown, trendStrengthScore: score);
            var r = LlmTimeframeSummaryBuilder.Build(s);

            r.TrendStrengthLabel.Should().Be(TrendStrengthLabel.Undefined,
                because: $"Unknown trend with score={score} must yield Undefined");
        }
    }

    // ─── EMA conflict: Bullish trend + emaBullish=false ⇒ Neutral bias ──────

    [Fact]
    public void Build_BullishTrendWithEmaConflict_BiasIsNeutral()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: false, isAboveEma200: true);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.Bias.Should().Be(TimeframeBias.Neutral,
            because: "Bullish trend without EMA alignment produces EMA conflict → Neutral bias");
        r.IsTrendConfirmed.Should().BeFalse();
        r.MomentumState.Should().Be(MomentumState.Neutral);
    }

    // ─── RiskFlags ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_WeakTrendStrengthScore_AddsWeakTrendRiskFlag()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, trendStrengthScore: 0.3m);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.RiskFlags.Should().Contain("WeakTrend");
    }

    [Fact]
    public void Build_LowVolumeRatio_AddsLowVolumeRiskFlag()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, trendStrengthScore: 0.6m, volumeRatio: 0.8m);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.RiskFlags.Should().Contain("LowVolume");
    }

    [Fact]
    public void Build_RsiOverbought_AddsRiskFlagAndOverextendedMomentum()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 75m, rsiOverbought: true);
        var r = LlmTimeframeSummaryBuilder.Build(s);

        r.RiskFlags.Should().Contain("RsiOverbought");
        r.MomentumState.Should().Be(MomentumState.Overextended);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static TimeframeAnalysisSnapshot MakeSnapshot(
        MarketTrend trend             = MarketTrend.Bullish,
        bool        emaBullish        = false,
        bool        emaBearish        = false,
        bool        isAboveEma200     = true,
        decimal     rsi14             = 55m,
        bool        rsiOverbought     = false,
        bool        rsiOversold       = false,
        decimal     trendStrengthScore = 0.6m,
        decimal     volumeRatio       = 1.1m,
        decimal?    distanceToSupport = 0.6m,
        decimal?    distanceToResist  = 0.3m) =>
        new()
        {
            Timeframe             = "1h",
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle            = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 100m, High = 105m, Low = 99m, Close = 104m,
                Volume = 1000m, Turnover = 104000m,
            },
            Ema20               = 102m,
            Ema50               = 101m,
            Ema200              = 98m,
            Rsi14               = rsi14,
            Atr14               = 2m,
            VolumeSma20         = 900m,
            VolumeRatio         = volumeRatio,
            TrendStrengthScore  = trendStrengthScore,
            Trend               = trend,
            Support1            = 99m,
            Support2            = 97m,
            Resistance1         = 106m,
            Resistance2         = 108m,
            IsAboveEma20        = true,
            IsAboveEma50        = true,
            IsAboveEma200       = isAboveEma200,
            EmaBullishAlignment = emaBullish,
            EmaBearishAlignment = emaBearish,
            RsiOverbought       = rsiOverbought,
            RsiOversold         = rsiOversold,
            CandleRangePct      = 0.06m,
            DistanceToSupport1Pct    = distanceToSupport,
            DistanceToResistance1Pct = distanceToResist,
        };
}








