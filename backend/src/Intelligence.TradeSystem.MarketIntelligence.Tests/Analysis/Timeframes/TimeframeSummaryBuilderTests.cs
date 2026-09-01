using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Timeframes;

/// <summary>
/// Прямые unit-тесты для <c>TimeframeSummaryBuilder</c>.
/// Работают без HTTP-стека — значительно быстрее endpoint-тестов.
/// Покрывают инварианты согласованности summary-полей.
/// </summary>
public sealed class TimeframeSummaryBuilderTests
{
    // ─── Bullish fully confirmed ─────────────────────────────────────────────

    [Fact]
    public void Build_BullishFullyConfirmed_AllSummaryFieldsAreConsistent()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m);

        var r = BuildForTest(s);

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
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m);

        var r = BuildForTest(s);

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

        var r = BuildForTest(s);

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

        var r = BuildForTest(s);

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
        var r = BuildForTest(s);

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
        var rb = BuildForTest(bullish);
        if (rb.IsTrendConfirmed)
            rb.Bias.Should().NotBe(TimeframeBias.Neutral);

        // Bearish confirmed
        var bearish = MakeSnapshot(trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false);
        var rr = BuildForTest(bearish);
        if (rr.IsTrendConfirmed)
            rr.Bias.Should().NotBe(TimeframeBias.Neutral);
    }

    // ─── Invariant: Healthy ⇒ IsTrendConfirmed=true ──────────────────────────

    [Fact]
    public void Build_WhenMomentumIsHealthy_IsTrendConfirmedIsTrue()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true, rsi14: 60m);
        var r = BuildForTest(s);

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
        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral,
            because: $"{trend} must always produce Neutral bias regardless of EMA flags");
    }

    // ─── Invariant: Unknown ⇒ TrendStrengthLabel=Undefined ──────────────────

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void Build_Unknown_TrendStrengthLabelIsUndefined_RegardlessOfScore(double scoreDouble)
    {
        var score = (decimal)scoreDouble;
        var s = MakeSnapshot(trend: MarketTrend.Unknown, trendStrengthScore: score);
        var r = BuildForTest(s);

        r.TrendStrengthLabel.Should().Be(TrendStrengthLabel.Undefined,
            because: $"Unknown trend with score={score} must yield Undefined");
    }

    // ─── EMA conflict: Bullish trend + emaBullish=false ⇒ Neutral bias ──────

    [Fact]
    public void Build_BullishTrendWithEmaConflict_BiasIsNeutral()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: false, isAboveEma200: true);
        var r = BuildForTest(s);

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
        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("WeakTrend");
    }

    [Fact]
    public void Build_LowVolumeRatio_AddsLowVolumeRiskFlag()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, trendStrengthScore: 0.6m, volumeRatio: 0.4m);
        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("LowVolume");
    }

    [Fact]
    public void Build_RsiOverbought_AddsRiskFlagAndOverextendedMomentum()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 75m, rsiOverbought: true);
        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("RsiOverbought");
        r.MomentumState.Should().Be(MomentumState.Overextended);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static TimeframeAnalysisSnapshot MakeSnapshot(
        MarketTrend trend = MarketTrend.Bullish,
        bool emaBullish = false,
        bool emaBearish = false,
        bool isAboveEma200 = true,
        bool isAboveEma20 = true,
        bool isAboveEma50 = true,
        decimal? rsi14 = 55m,
        bool rsiOverbought = false,
        bool rsiOversold = false,
        decimal trendStrengthScore = 0.6m,
        decimal volumeRatio = 1.1m,
        decimal? distanceToSupport = 0.6m,
        decimal? distanceToResist = 0.3m,
        // Level strengths (default Strong — не ограничивает Good)
        decimal? support1Strength = 0.8m,
        decimal? resistance1Strength = 0.8m,
        // Indicator availability / fallback
        bool rsi14IsReliable = true,
        bool emaIsReliable = true,
        bool emaHasFallback = false,
        bool atrIsReliable = true,
        bool atrIsFallback = false,
        bool volumeRatioIsReliable = true,
        bool volumeRatioIsFallback = false) =>
        new()
        {
            Timeframe = "1h",
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 100m,
                High = 105m,
                Low = 99m,
                Close = 104m,
                Volume = 1000m,
                Turnover = 104000m,
            },
            Ema20 = 102m,
            Ema50 = 101m,
            Ema200 = 98m,
            Rsi14 = rsi14,
            Rsi14IsReliable = rsi14IsReliable,
            Atr14 = 2m,
            VolumeSma20 = 900m,
            VolumeRatio = volumeRatio,
            TrendStrengthScore = trendStrengthScore,
            Trend = trend,
            Support1 = 99m,
            Support1Strength = support1Strength,
            Support2 = 97m,
            Resistance1 = 106m,
            Resistance1Strength = resistance1Strength,
            Resistance2 = 108m,
            IsAboveEma20 = isAboveEma20,
            IsAboveEma50 = isAboveEma50,
            IsAboveEma200 = isAboveEma200,
            EmaBullishAlignment = emaBullish,
            EmaBearishAlignment = emaBearish,
            RsiOverbought = rsiOverbought,
            RsiOversold = rsiOversold,
            EmaIsReliable = emaIsReliable,
            EmaHasFallback = emaHasFallback,
            AtrIsReliable = atrIsReliable,
            AtrIsFallback = atrIsFallback,
            VolumeRatioIsReliable = volumeRatioIsReliable,
            VolumeRatioIsFallback = volumeRatioIsFallback,
            CandleRangePct = 0.06m,
            DistanceToSupport1Pct = distanceToSupport,
            DistanceToResistance1Pct = distanceToResist,
        };

    /// <summary>
    /// Test helper: вызывает <c>TimeframeSummaryBuilder.Build</c> с безопасными дефолтами
    /// (<c>snapshotIsFresh = true</c>, <c>marketRegime = MarketRegimes.Trending</c>).
    /// Используется для тестов, которые проверяют структурные инварианты,
    /// а не поведение параметров свежести/режима.
    /// </summary>
    private static TimeframeSummary BuildForTest(
        TimeframeAnalysisSnapshot s,
        bool snapshotIsFresh = true,
        string? marketRegime = MarketRegimes.Trending,
        NearestOppositeLevel? higherTfOppositeLevel = null) =>
        TimeframeSummaryBuilder.Build(s, snapshotIsFresh, marketRegime, higherTfOppositeLevel);

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 11: Nullable / unavailable indicator scenarios
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── 13.1: EMA unavailable ───────────────────────────────────────────────

    [Fact]
    public void Build_EmaUnavailable_NeutralSummaryAndIndicatorUnavailableFlag()
    {
        // When EMA unavailable the assembler sets Trend=Unknown; we replicate that here.
        var s = MakeSnapshot(
            trend: MarketTrend.Unknown,
            trendStrengthScore: 0m,
            emaIsReliable: false,
            emaHasFallback: false,
            emaBullish: false, emaBearish: false);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.IsTrendConfirmed.Should().BeFalse();
        r.MomentumState.Should().Be(MomentumState.Neutral);
        r.EntryQuality.Should().Be(EntryQuality.Poor);
        r.RiskFlags.Should().Contain("IndicatorUnavailable");
    }

    // ─── 13.2: RSI unavailable — must not become oversold ────────────────────

    [Fact]
    public void Build_RsiUnavailable_NoOversoldOrOverboughtFlags_EntryQualityNotGood()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            rsi14: null, rsi14IsReliable: false,
            rsiOversold: false, rsiOverbought: false,
            trendStrengthScore: 0.85m);

        var r = BuildForTest(s);

        r.RiskFlags.Should().NotContain("RsiOversold", because: "RSI unavailable must not produce RsiOversold");
        r.RiskFlags.Should().NotContain("RsiOverbought", because: "RSI unavailable must not produce RsiOverbought");
        r.RiskFlags.Should().Contain("RsiUnavailable");
        r.RiskFlags.Should().Contain("IndicatorUnavailable");
        r.EntryQuality.Should().Be(EntryQuality.Poor,
            because: "RSI unavailable caps entryQuality at Poor");
    }

    // ─── 13.3: ATR unavailable ───────────────────────────────────────────────

    [Fact]
    public void Build_AtrUnavailable_AddsAtrUnavailableFlagAndCapsEntryQuality()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m,
            atrIsReliable: false);

        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("AtrUnavailable");
        r.RiskFlags.Should().Contain("IndicatorUnavailable");
        r.EntryQuality.Should().Be(EntryQuality.Poor,
            because: "ATR unavailable caps entryQuality at Poor");
    }

    // ─── 13.4: VolumeRatio unavailable — no fake LowVolume ───────────────────

    [Fact]
    public void Build_VolumeRatioUnavailable_NoLowVolumeFlag_AddsVolumeDataUnavailable()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, trendStrengthScore: 0.6m,
            volumeRatio: 0m,           // zero because unavailable
            volumeRatioIsReliable: false);

        var r = BuildForTest(s);

        r.RiskFlags.Should().NotContain("LowVolume",
            because: "VolumeRatio=0 from unavailable source must not trigger LowVolume");
        r.RiskFlags.Should().Contain("VolumeDataUnavailable");
        r.RiskFlags.Should().Contain("IndicatorUnavailable");
    }

    // ─── 13.5: VolumeRatio fallback — VolumeDataFallback + conditional LowVolume

    [Fact]
    public void Build_VolumeRatioFallback_Low_AddsBothVolumeDataFallbackAndLowVolume()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, trendStrengthScore: 0.6m,
            volumeRatio: 0.3m,
            volumeRatioIsReliable: true, volumeRatioIsFallback: true);

        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("VolumeDataFallback");
        r.RiskFlags.Should().Contain("LowVolume",
            because: "fallback VolumeRatio < 0.5 still triggers LowVolume");
        r.RiskFlags.Should().Contain("IndicatorFallback");
    }

    [Fact]
    public void Build_VolumeRatioFallback_High_OnlyVolumeDataFallbackFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, trendStrengthScore: 0.6m,
            volumeRatio: 0.8m,
            volumeRatioIsReliable: true, volumeRatioIsFallback: true);

        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("VolumeDataFallback");
        r.RiskFlags.Should().NotContain("LowVolume",
            because: "fallback VolumeRatio 0.8 >= 0.5, no LowVolume");
    }

    // ─── 13.6: Strong bullish but RSI unavailable ─────────────────────────────

    [Fact]
    public void Build_BullishConfirmed_RsiUnavailable_MomentumNotHealthy_EntryNotGood()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: null, rsi14IsReliable: false,
            trendStrengthScore: 0.85m);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Bullish,
            because: "EMA alignment + Bullish trend → Bullish bias even without RSI");
        r.IsTrendConfirmed.Should().BeTrue(
            because: "EMA available and trend confirmed — RSI is not required for confirmation");
        r.MomentumState.Should().NotBe(MomentumState.Healthy,
            because: "RSI unavailable → Healthy momentum cannot be confirmed");
        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "RSI unavailable caps entryQuality at Poor");
        r.RiskFlags.Should().Contain("RsiUnavailable");
    }

    // ─── 13.7: Normal fully available bullish ────────────────────────────────

    [Fact]
    public void Build_FullyAvailable_Bullish_SummaryConsistent()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToSupport: 0.5m,
            rsi14IsReliable: true, emaIsReliable: true, atrIsReliable: true, volumeRatioIsReliable: true);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Bullish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.MomentumState.Should().Be(MomentumState.Healthy);
        r.EntryQuality.Should().Be(EntryQuality.Good,
            because: "all indicators available, confirmed bullish, RSI healthy, price near support");
        r.RiskFlags.Should().NotContain("IndicatorUnavailable");
        r.RiskFlags.Should().NotContain("RsiUnavailable");
        r.RiskFlags.Should().NotContain("AtrUnavailable");
        r.RiskFlags.Should().NotContain("VolumeDataUnavailable");
    }

    // ─── 13.8: Normal fully available bearish ────────────────────────────────

    [Fact]
    public void Build_FullyAvailable_Bearish_SummaryConsistent()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToResist: 0.5m,
            rsi14IsReliable: true, emaIsReliable: true, atrIsReliable: true, volumeRatioIsReliable: true);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Bearish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.MomentumState.Should().Be(MomentumState.Healthy);
        r.EntryQuality.Should().Be(EntryQuality.Good,
            because: "all indicators available, confirmed bearish, RSI healthy, price near resistance");
        r.RiskFlags.Should().NotContain("IndicatorUnavailable");
    }

    // ─── 13.9: EMA fallback alone caps EntryQuality at Fair ─────────────────

    [Fact]
    public void Build_EmaFallback_Alone_Caps_EntryQuality_At_Fair_Not_Good()
    {
        // Все критические индикаторы доступны, но EMA рассчитаны по partial window (fallback).
        // ApplyIndicatorCap должен не допустить Good — только Fair.
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, rsi14IsReliable: true,
            trendStrengthScore: 0.85m,
            distanceToSupport: 0.5m,
            emaIsReliable: true, emaHasFallback: true,   // EMA partial window
            atrIsReliable: true, atrIsFallback: false,
            volumeRatioIsReliable: true, volumeRatioIsFallback: false,
            volumeRatio: 1.2m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().Be(EntryQuality.Fair,
            because: "EMA fallback caps entryQuality at Fair — Good is not allowed when EMA uses partial window");
        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "ApplyIndicatorCap must block Good when EmaHasFallback=true");
        r.RiskFlags.Should().Contain("IndicatorFallback",
            because: "EMA fallback must produce IndicatorFallback risk flag");
    }

    // ─── 13.10: ATR fallback alone caps EntryQuality at Fair ─────────────────

    [Fact]
    public void Build_AtrFallback_Alone_Caps_EntryQuality_At_Fair_Not_Good()
    {
        // Все критические индикаторы доступны, ATR рассчитан по partial window (fallback).
        // ApplyIndicatorCap: AtrIsFallback || EmaHasFallback → cap at Fair.
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, rsi14IsReliable: true,
            trendStrengthScore: 0.85m,
            distanceToSupport: 0.5m,
            emaIsReliable: true, emaHasFallback: false,
            atrIsReliable: true, atrIsFallback: true,    // ATR partial window
            volumeRatioIsReliable: true, volumeRatioIsFallback: false,
            volumeRatio: 1.2m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().Be(EntryQuality.Fair,
            because: "ATR fallback caps entryQuality at Fair — Good is not allowed when AtrIsFallback=true");
        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "ApplyIndicatorCap must block Good when AtrIsFallback=true");
        r.RiskFlags.Should().Contain("AtrFallback",
            because: "ATR fallback must produce AtrFallback risk flag");
        r.RiskFlags.Should().Contain("IndicatorFallback",
            because: "any fallback indicator must produce IndicatorFallback risk flag");
    }

    // ─── 13.11: VolumeRatio fallback does NOT affect EntryQuality ────────────

    [Fact]
    public void Build_VolumeRatioFallback_Does_Not_Affect_EntryQuality()
    {
        // Намеренное решение дизайна: VolumeRatioFallback — вспомогательный сигнал,
        // не является ограничивающим фактором для entryQuality.
        // При всех остальных доступных индикаторах Good должен оставаться возможным.
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, rsi14IsReliable: true,
            trendStrengthScore: 0.85m,
            distanceToSupport: 0.5m,
            emaIsReliable: true, emaHasFallback: false,
            atrIsReliable: true, atrIsFallback: false,
            volumeRatioIsReliable: true, volumeRatioIsFallback: true,  // fallback, but high ratio
            volumeRatio: 1.2m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().Be(EntryQuality.Good,
            because: "VolumeRatioFallback must not cap entryQuality — volume is an auxiliary signal only");
        r.RiskFlags.Should().Contain("VolumeDataFallback",
            because: "fallback volume still produces VolumeDataFallback risk flag");
        r.RiskFlags.Should().Contain("IndicatorFallback");
        r.RiskFlags.Should().NotContain("LowVolume",
            because: "volumeRatio=1.2 >= 0.5 threshold, no LowVolume flag");
    }

    // ─── 14: Integration — RSI unavailable + EMA200 fallback + ATR unavailable

    [Fact]
    public void Build_Integration_RsiUnavailable_Ema200Fallback_AtrUnavailable_SafeSummary()
    {
        // EMA200 is fallback but still has a value → Trend can still be determined.
        // RSI and ATR are unavailable.
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: null, rsi14IsReliable: false,
            trendStrengthScore: 0.82m,
            emaIsReliable: true, emaHasFallback: true,  // EMA200 fallback
            atrIsReliable: false,
            volumeRatioIsReliable: true, volumeRatio: 1.0m);

        var r = BuildForTest(s);

        // No false oversold/overbought
        r.RiskFlags.Should().NotContain("RsiOversold", because: "RSI unavailable must not produce RsiOversold");
        r.RiskFlags.Should().NotContain("RsiOverbought");

        // Specific indicator flags present
        r.RiskFlags.Should().Contain("RsiUnavailable");
        r.RiskFlags.Should().Contain("AtrUnavailable");
        r.RiskFlags.Should().Contain("IndicatorUnavailable");
        r.RiskFlags.Should().Contain("IndicatorFallback", because: "EMA200 is fallback");

        // EntryQuality capped — multiple unavailable indicators
        r.EntryQuality.Should().Be(EntryQuality.Poor,
            because: "RSI and ATR unavailable → entry quality capped at Poor");

        // Momentum not Healthy without RSI confirmation
        r.MomentumState.Should().NotBe(MomentumState.Healthy,
            because: "RSI unavailable prevents Healthy momentum");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Higher TF opposite level + entry level strength scenarios
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── 14.1: Bullish �� higher TF resistance very close → Poor ─────────────

    [Fact]
    public void Build_Bullish_HigherTfResistanceVeryClose_Returns_Poor_And_Flag()
    {
        // m15-like: current TF has no resistance, but higher TF resistance at 0.05%
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: null, resistance1Strength: null);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.05m, Strength: 0.85m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.EntryQuality.Should().Be(EntryQuality.Poor,
            because: "higher TF strong resistance < 0.15% → Poor");
        r.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    // ─── 14.2: Bullish — higher TF resistance near (0.25%) → not Good ────────

    [Fact]
    public void Build_Bullish_HigherTfResistanceNear_Returns_AtMostFair_And_Flag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: null, resistance1Strength: null);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 0.85m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "higher TF resistance < 0.30% → Good forbidden");
        r.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    // ─── 14.3: Bearish — higher TF support very close → Poor ────────────────

    [Fact]
    public void Build_Bearish_HigherTfSupportVeryClose_Returns_Poor_And_Flag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m,
            distanceToSupport: null, support1Strength: null);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.05m, Strength: 0.85m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.EntryQuality.Should().Be(EntryQuality.Poor,
            because: "higher TF strong support < 0.15% → Poor");
        r.RiskFlags.Should().Contain("NearHigherTimeframeSupport");
    }

    // ─── 14.4: Bearish — higher TF support near (0.25%) → not Good ──────��───

    [Fact]
    public void Build_Bearish_HigherTfSupportNear_Returns_AtMostFair_And_Flag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m,
            distanceToSupport: null, support1Strength: null);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 0.85m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "higher TF support < 0.30% → Good forbidden");
        r.RiskFlags.Should().Contain("NearHigherTimeframeSupport");
    }

    // ─── 14.5: WeakEntryLevel risk flag ──────────────────────────────────────

    [Fact]
    public void Build_Bullish_WeakSupport_AddsWeakEntryLevelFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.20m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "Weak support (0.20 ≤ 0.35) → not above Fair");
        r.RiskFlags.Should().Contain("WeakEntryLevel");
    }

    [Fact]
    public void Build_Bearish_WeakResistance_AddsWeakEntryLevelFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0.5m, resistance1Strength: 0.20m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "Weak resistance (0.20 ≤ 0.35) → not above Fair");
        r.RiskFlags.Should().Contain("WeakEntryLevel");
    }

    // ─── 14.6: Regression — Good still reachable without obstacles ───────────

    [Fact]
    public void Build_Bullish_StrongSupport_NoOppLevel_Returns_Good()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: null, resistance1Strength: null);

        var r = BuildForTest(s);

        r.EntryQuality.Should().Be(EntryQuality.Good,
            because: "strong support + no resistance obstacle → Good reachable");
        r.RiskFlags.Should().NotContain("WeakEntryLevel");
    }

    [Fact]
    public void Build_Bearish_StrongResistance_NoOppLevel_Returns_Good()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m,
            distanceToSupport: null, support1Strength: null);

        var r = BuildForTest(s);

        r.EntryQuality.Should().Be(EntryQuality.Good,
            because: "strong resistance + no support obstacle → Good reachable");
        r.RiskFlags.Should().NotContain("WeakEntryLevel");
    }

    // ─── 14.7: NearResistance flag (same TF) ─────────────────────────────────

    [Fact]
    public void Build_Bullish_SameTfResistanceNear_AddsNearResistanceFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: 0.20m, resistance1Strength: 0.85m);

        var r = BuildForTest(s);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "same-TF resistance < 0.30% → Good forbidden");
        r.RiskFlags.Should().Contain("NearResistance");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RiskFlags synchronization scenarios (Prompts 14 сценариев 1–7 + BTCUSDT)
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── Scenario 1: h4 bearish confirmed but entry filtered ──────────────────

    [Fact]
    public void RiskFlags_Scenario1_H4BearishConfirmedButEntryFiltered()
    {
        // bias = Bearish, isTrendConfirmed = true, entryQuality = Poor
        // isAboveEma20 = true, isAboveEma50 = true, rsi14 > 50, momentumState = Weak
        // marketRegime = Neutral, snapshotIsFresh = true, volumeRatio >= 0.5
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: true, isAboveEma50: true,
            rsi14: 53.7m, trendStrengthScore: 0.60m,
            volumeRatio: 0.8m,
            distanceToResist: 0.5m, resistance1Strength: 0.75m,
            distanceToSupport: 0.3m, support1Strength: 0.70m);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Neutral);

        r.Bias.Should().Be(TimeframeBias.Bearish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.RiskFlags.Should().Contain("AboveEma20");
        r.RiskFlags.Should().Contain("AboveEma50");
        r.RiskFlags.Should().Contain("EmaConflict");
        r.RiskFlags.Should().Contain("RsiAgainstBearishBias",
            because: "rsi14=53.7 > 50 opposes bearish direction");
        r.RiskFlags.Should().Contain("WeakMomentum",
            because: "momentumState=Weak for Bearish bias with rsi14 not in healthy range");
        r.RiskFlags.Should().Contain("NeutralMarketRegime");
        r.RiskFlags.Should().Contain("TrendConfirmedButEntryFiltered",
            because: "trend confirmed but entry quality not Good");
        // Uniqueness
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 2: m15 neutral low volume near resistance ───────────────────

    [Fact]
    public void RiskFlags_Scenario2_M15NeutralLowVolumeNearResistance()
    {
        // trend = Sideways, bias = Neutral, volumeRatio < 0.25, distance < 0.15, entryQuality = Poor
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways,
            rsi14: 50m, trendStrengthScore: 0.3m,
            volumeRatio: 0.18m,
            distanceToResist: 0.12m, resistance1Strength: 0.55m,
            distanceToSupport: 0.5m, support1Strength: 0.70m,
            isAboveEma20: false, isAboveEma50: false);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.RiskFlags.Should().Contain("VeryLowVolume",
            because: "volumeRatio=0.18 < 0.25 → VeryLowVolume");
        r.RiskFlags.Should().Contain("WeakTrend");
        r.RiskFlags.Should().Contain("NearResistance",
            because: "resistance1 at 0.12% < 0.30% threshold for Neutral bias");
        r.RiskFlags.Should().Contain("RangeBound");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 3: h1 neutral range between support/resistance ──────────────

    [Fact]
    public void RiskFlags_Scenario3_H1NeutralRangeBetweenLevels()
    {
        // trend = Sideways, support strong, resistance moderate, distToResist < 0.3, volumeRatio < 0.5
        // mixed EMA state: isAboveEma20 = true, isAboveEma50 = false
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways,
            rsi14: 50m, trendStrengthScore: 0.3m,
            volumeRatio: 0.32m,
            distanceToResist: 0.06m, resistance1Strength: 0.55m,
            distanceToSupport: 0.49m, support1Strength: 0.80m,
            isAboveEma20: true, isAboveEma50: false);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.RiskFlags.Should().Contain("LowVolume",
            because: "volumeRatio=0.32 < 0.50");
        r.RiskFlags.Should().Contain("WeakTrend");
        r.RiskFlags.Should().Contain("NearResistance",
            because: "resistance at 0.06% < 0.30%");
        r.RiskFlags.Should().Contain("RangeBound");
        r.RiskFlags.Should().Contain("MixedEmaState",
            because: "isAboveEma20=true but isAboveEma50=false");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 4: bullish entry filtered by resistance ─────────────────────

    [Fact]
    public void RiskFlags_Scenario4_BullishEntryFilteredByNearResistance()
    {
        // bias = Bullish, confirmed, entryQuality Poor/Fair, resistance < 0.3%, volume normal, EMA confirms
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            isAboveEma20: true, isAboveEma50: true,
            rsi14: 60m, trendStrengthScore: 0.85m,
            volumeRatio: 1.0m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: 0.20m, resistance1Strength: 0.75m);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);

        r.Bias.Should().Be(TimeframeBias.Bullish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.RiskFlags.Should().Contain("NearResistance");
        r.RiskFlags.Should().Contain("TrendConfirmedButEntryFiltered");
        r.RiskFlags.Should().NotContain("EmaConflict",
            because: "EMA fully confirms bullish direction");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 5: bearish entry filtered by nearby support ─────────────────

    [Fact]
    public void RiskFlags_Scenario5_BearishEntryFilteredByNearSupport()
    {
        // bias = Bearish, confirmed, entryQuality Poor/Fair, support < 0.3%, volume normal, EMA confirms
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 40m, trendStrengthScore: 0.85m,
            volumeRatio: 1.0m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m,
            distanceToSupport: 0.20m, support1Strength: 0.75m);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);

        r.Bias.Should().Be(TimeframeBias.Bearish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.RiskFlags.Should().Contain("NearSupport");
        r.RiskFlags.Should().Contain("TrendConfirmedButEntryFiltered");
        r.RiskFlags.Should().NotContain("EmaConflict",
            because: "EMA fully confirms bearish direction");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 6: clean bullish Good setup ─────────────────────────────────

    [Fact]
    public void RiskFlags_Scenario6_CleanBullishGoodSetup_NoSpuriousFlags()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            isAboveEma20: true, isAboveEma50: true,
            rsi14: 60m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: null, resistance1Strength: null);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);

        r.EntryQuality.Should().Be(EntryQuality.Good);
        r.RiskFlags.Should().NotContain("LowVolume");
        r.RiskFlags.Should().NotContain("VeryLowVolume");
        r.RiskFlags.Should().NotContain("EmaConflict");
        r.RiskFlags.Should().NotContain("BelowEma20");
        r.RiskFlags.Should().NotContain("BelowEma50");
        r.RiskFlags.Should().NotContain("NearResistance");
        r.RiskFlags.Should().NotContain("TrendConfirmedButEntryFiltered");
        r.RiskFlags.Should().NotContain("StaleSnapshot");
        r.RiskFlags.Should().NotContain("NeutralMarketRegime");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── Scenario 7: clean bearish Good setup ─────────────────────────────────

    [Fact]
    public void RiskFlags_Scenario7_CleanBearishGoodSetup_NoSpuriousFlags()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m,
            distanceToSupport: null, support1Strength: null);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);

        r.EntryQuality.Should().Be(EntryQuality.Good);
        r.RiskFlags.Should().NotContain("LowVolume");
        r.RiskFlags.Should().NotContain("VeryLowVolume");
        r.RiskFlags.Should().NotContain("EmaConflict");
        r.RiskFlags.Should().NotContain("AboveEma20");
        r.RiskFlags.Should().NotContain("AboveEma50");
        r.RiskFlags.Should().NotContain("NearSupport");
        r.RiskFlags.Should().NotContain("TrendConfirmedButEntryFiltered");
        r.RiskFlags.Should().NotContain("StaleSnapshot");
        r.RiskFlags.Should().NotContain("NeutralMarketRegime");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── BTCUSDT-like regression: m15 neutral ─────────────────────────────────

    [Fact]
    public void RiskFlags_BtcUsdt_M15_NeutralLowVolumeNearResistance()
    {
        // trend Sideways, bias Neutral, volumeRatio 0.1883, distanceToResistance1Pct 0.1064, resistance Moderate
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways,
            rsi14: 50m, trendStrengthScore: 0.3m,
            volumeRatio: 0.1883m,
            distanceToResist: 0.1064m, resistance1Strength: 0.55m,
            distanceToSupport: 0.5m, support1Strength: 0.70m);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.EntryQuality.Should().Be(EntryQuality.Poor);
        r.RiskFlags.Should().Contain("VeryLowVolume",
            because: "0.1883 < 0.25 → VeryLowVolume");
        r.RiskFlags.Should().Contain("WeakTrend");
        r.RiskFlags.Should().Contain("NearResistance",
            because: "0.1064 < 0.30 for Neutral bias");
        r.RiskFlags.Should().Contain("RangeBound");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── BTCUSDT-like regression: h1 neutral range ────────────────────────────

    [Fact]
    public void RiskFlags_BtcUsdt_H1_NeutralRangeNearResistanceMixedEma()
    {
        // trend Sideways, volumeRatio 0.3248, distanceToResistance1Pct 0.06, mixed EMA
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways,
            rsi14: 50m, trendStrengthScore: 0.3m,
            volumeRatio: 0.3248m,
            distanceToResist: 0.06m, resistance1Strength: 0.55m,
            distanceToSupport: 0.49m, support1Strength: 0.80m,
            isAboveEma20: true, isAboveEma50: false);  // mixed EMA state

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.EntryQuality.Should().Be(EntryQuality.Poor);
        r.RiskFlags.Should().Contain("LowVolume",
            because: "0.3248 is in [0.25, 0.50) → LowVolume");
        r.RiskFlags.Should().Contain("WeakTrend");
        r.RiskFlags.Should().Contain("NearResistance",
            because: "0.06 < 0.30");
        r.RiskFlags.Should().Contain("RangeBound");
        r.RiskFlags.Should().Contain("MixedEmaState",
            because: "isAboveEma20=true but isAboveEma50=false");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── BTCUSDT-like regression: h4 bearish confirmed filtered ───────────────

    [Fact]
    public void RiskFlags_BtcUsdt_H4_BearishConfirmedAllFiltersFired()
    {
        // trend Bearish, bias Bearish, isTrendConfirmed true, entryQuality Poor
        // isAboveEma20=true, isAboveEma50=true, rsi14=53.7, momentumState=Weak, marketRegime=Neutral
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: true, isAboveEma50: true,
            rsi14: 53.7m, trendStrengthScore: 0.60m,
            volumeRatio: 0.8m,
            distanceToResist: 0.5m, resistance1Strength: 0.75m,
            distanceToSupport: 0.3m, support1Strength: 0.70m);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Neutral);

        r.Bias.Should().Be(TimeframeBias.Bearish);
        r.IsTrendConfirmed.Should().BeTrue();
        r.RiskFlags.Should().Contain("AboveEma20");
        r.RiskFlags.Should().Contain("AboveEma50");
        r.RiskFlags.Should().Contain("EmaConflict");
        r.RiskFlags.Should().Contain("RsiAgainstBearishBias",
            because: "rsi14=53.7 > 50");
        r.RiskFlags.Should().Contain("WeakMomentum");
        r.RiskFlags.Should().Contain("NeutralMarketRegime");
        r.RiskFlags.Should().Contain("DirectionalTrendWithNeutralRegime",
            because: "isTrendConfirmed=true + Neutral regime");
        r.RiskFlags.Should().Contain("TrendConfirmedButEntryFiltered");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── VeryLowVolume threshold boundary ─────────────────────────────────────

    [Fact]
    public void RiskFlags_VeryLowVolume_Below_0_25_AddsBothVeryLowVolumeAndLowVolume()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, volumeRatio: 0.18m, trendStrengthScore: 0.6m);
        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("VeryLowVolume",
            because: "volumeRatio=0.18 < 0.25 → VeryLowVolume");
        r.RiskFlags.Should().Contain("LowVolume",
            because: "VeryLowVolume implies LowVolume — they are additive, not mutually exclusive");
    }

    [Fact]
    public void RiskFlags_LowVolume_Between_0_25_And_0_50()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, volumeRatio: 0.32m, trendStrengthScore: 0.6m);
        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("LowVolume");
        r.RiskFlags.Should().NotContain("VeryLowVolume");
    }

    // ─── StaleSnapshot flag ────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RiskFlags_StaleSnapshot_PresentOnlyWhenStale(bool snapshotIsFresh)
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.0m);

        var r = TimeframeSummaryBuilder.Build(s, snapshotIsFresh: snapshotIsFresh, marketRegime: MarketRegimes.Trending);

        r.RiskFlags.Contains("StaleSnapshot").Should().Be(!snapshotIsFresh,
            because: "StaleSnapshot flag is present iff snapshotIsFresh=false");
    }

    // ─── RsiAgainstBias flags ──────────────────────────────────────────────────

    [Fact]
    public void RiskFlags_Bullish_Rsi_Below50_AddsRsiAgainstBullishBias()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 45m, trendStrengthScore: 0.85m, volumeRatio: 1.0m,
            distanceToSupport: 0.5m, support1Strength: 0.80m);

        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("RsiAgainstBullishBias");
        r.RiskFlags.Should().NotContain("RsiAgainstBearishBias");
    }

    [Fact]
    public void RiskFlags_Bearish_Rsi_Above50_AddsRsiAgainstBearishBias()
    {
        var s = MakeSnapshot(trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 55m, trendStrengthScore: 0.85m, volumeRatio: 1.0m,
            distanceToResist: 0.5m, resistance1Strength: 0.80m);

        var r = BuildForTest(s);

        r.RiskFlags.Should().Contain("RsiAgainstBearishBias");
        r.RiskFlags.Should().NotContain("RsiAgainstBullishBias");
    }

    // ─── TrendConfirmedButEntryFiltered not added when Good ───────────────────

    [Fact]
    public void RiskFlags_TrendConfirmedGoodEntry_NoTrendConfirmedButEntryFilteredFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            isAboveEma20: true, isAboveEma50: true,
            rsi14: 60m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.80m,
            distanceToResist: null, resistance1Strength: null);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: MarketRegimes.Trending);

        r.EntryQuality.Should().Be(EntryQuality.Good);
        r.RiskFlags.Should().NotContain("TrendConfirmedButEntryFiltered");
    }

    // ─── MissingEntryLevel flag ────────────────────────────────────────────────

    [Fact]
    public void RiskFlags_Bullish_NullSupport_AddsMissingEntryLevelFlag()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.0m,
            distanceToSupport: null, support1Strength: null,
            distanceToResist: null, resistance1Strength: null);
        // Override support to null via a snapshot with no support
        var sNoSupport = s with { Support1 = null, Support1Strength = null };

        var r = BuildForTest(sNoSupport);

        r.RiskFlags.Should().Contain("MissingEntryLevel");
    }

    // ─── NeutralBias flag ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(MarketTrend.Sideways)]
    [InlineData(MarketTrend.Unknown)]
    public void RiskFlags_NeutralBias_AddsBothNeutralBiasAndRangeBound(MarketTrend trend)
    {
        var s = MakeSnapshot(trend: trend, trendStrengthScore: 0.4m);
        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.RiskFlags.Should().Contain("NeutralBias",
            because: "bias == Neutral must produce NeutralBias flag");
        r.RiskFlags.Should().Contain("RangeBound",
            because: "NeutralBias and RangeBound are always co-present");
        r.RiskFlags.Should().OnlyHaveUniqueItems();
    }

    // ─── marketRegime trim: leading/trailing spaces ────────────────────────────

    [Theory]
    [InlineData(" Neutral")]
    [InlineData("Neutral ")]
    [InlineData(" Neutral ")]
    [InlineData("  NEUTRAL  ")]
    public void RiskFlags_MarketRegimeWithWhitespace_StillRecognizedAsNeutral(string paddedRegime)
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.0m);

        var r = TimeframeSummaryBuilder.Build(s,
            snapshotIsFresh: true,
            marketRegime: paddedRegime);

        r.RiskFlags.Should().Contain("NeutralMarketRegime",
            because: $"marketRegime='{paddedRegime}' should be recognized as Neutral after Trim()");
    }

    // ─── BetweenStrongSupportAndResistance threshold 0.75% ───────────────────

    [Fact]
    public void RiskFlags_BetweenStrongSupportAndResistance_At_0_74Pct_AddsFlag()
    {
        // Both distances just inside the 0.75% threshold; both levels Moderate/Strong.
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways, trendStrengthScore: 0.4m,
            volumeRatio: 1.0m,
            distanceToSupport: 0.74m, support1Strength: 0.60m,   // Moderate
            distanceToResist: 0.74m, resistance1Strength: 0.60m); // Moderate

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.RiskFlags.Should().Contain("BetweenStrongSupportAndResistance",
            because: "both distances 0.74% < 0.75% with Moderate levels → flag present");
    }

    [Fact]
    public void RiskFlags_BetweenStrongSupportAndResistance_At_0_76Pct_DoesNotAddFlag()
    {
        // Both distances just outside the 0.75% threshold.
        var s = MakeSnapshot(
            trend: MarketTrend.Sideways, trendStrengthScore: 0.4m,
            volumeRatio: 1.0m,
            distanceToSupport: 0.76m, support1Strength: 0.60m,
            distanceToResist: 0.76m, resistance1Strength: 0.60m);

        var r = BuildForTest(s);

        r.Bias.Should().Be(TimeframeBias.Neutral);
        r.RiskFlags.Should().NotContain("BetweenStrongSupportAndResistance",
            because: "both distances 0.76% >= 0.75% → flag absent");
    }

    // ─── Negative distance filtering in ResolveNearestOppositeLevel ──────────

    [Fact]
    public void Build_Bullish_NegativeCurrentResistance_PositiveHigherTf_SelectsHigherTf()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: -0.30m, resistance1Strength: 1m,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 1m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.RiskFlags.Should().Contain("NearHigherTimeframeResistance",
            because: "negative current resistance must be discarded; valid higher-TF resistance at 0.25% is the obstacle");
        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "higher-TF resistance at 0.25% < 0.30% caps quality at Fair");
    }

    [Fact]
    public void Build_Bearish_NegativeCurrentSupport_PositiveHigherTf_SelectsHigherTf()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false,
            rsi14: 38m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: -0.30m, support1Strength: 1m,
            distanceToResist: 0.5m, resistance1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 1m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.RiskFlags.Should().Contain("NearHigherTimeframeSupport",
            because: "negative current support must be discarded; valid higher-TF support at 0.25% is the obstacle");
        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "higher-TF support at 0.25% < 0.30% caps quality at Fair");
    }

    [Fact]
    public void Build_Bullish_BothPositive_CurrentCloser_SelectsCurrent()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0.10m, resistance1Strength: 0.8m,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 0.8m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.RiskFlags.Should().Contain("NearResistance",
            because: "current TF resistance at 0.10% is closer and positive → current TF obstacle wins");
        r.RiskFlags.Should().NotContain("NearHigherTimeframeResistance",
            because: "current TF obstacle wins, not higher TF");
    }

    [Fact]
    public void Build_Bullish_NegativeCurrent_NoHigherTf_NoOppositeLevel()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: -0.30m, resistance1Strength: 1m,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var r = BuildForTest(s, higherTfOppositeLevel: null);

        r.RiskFlags.Should().NotContain("NearResistance",
            because: "negative current resistance is invalid; no higher-TF candidate either");
        r.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void Build_Bullish_NullCurrent_NegativeHigherTf_NoOppositeLevel()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: null, resistance1Strength: null,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: -0.30m, Strength: 1m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.RiskFlags.Should().NotContain("NearHigherTimeframeResistance",
            because: "negative higher-TF distance is invalid; null current → no obstacle at all");
        r.RiskFlags.Should().NotContain("NearResistance");
    }

    [Fact]
    public void Build_Bullish_ZeroCurrentResistance_IsValidObstacle()
    {
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: 0m, resistance1Strength: 0.8m,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 0.8m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.RiskFlags.Should().Contain("NearResistance",
            because: "distance 0 is valid (resistance at price); current TF (0) <= higher TF (0.25) → current wins");
        r.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void Build_Bullish_NegativeCurrentResistance_ValidHigherTf_QualityIsNotGoodAndFlagIsSet()
    {
        // Before fix: negative current resistance (-0.30) numerically beat higher-TF 0.25 → higher-TF lost.
        // After fix: negative current discarded → higher-TF resistance caps quality at Fair.
        var s = MakeSnapshot(
            trend: MarketTrend.Bullish, emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToResist: -0.30m, resistance1Strength: 1m,
            distanceToSupport: 0.5m, support1Strength: 0.8m);

        var higherTf = new NearestOppositeLevel(DistancePct: 0.25m, Strength: 1m);
        var r = BuildForTest(s, higherTfOppositeLevel: higherTf);

        r.EntryQuality.Should().NotBe(EntryQuality.Good,
            because: "higher-TF resistance at 0.25% < NearOppositeThreshold (0.30%) must cap at Fair");
        r.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
        r.RiskFlags.Should().Contain("TrendConfirmedButEntryFiltered",
            because: "trend is confirmed (Bullish + EMA aligned) but entry is filtered by higher-TF obstacle");
    }

    [Fact]
    public void BuildWithHigherTimeframes_Bullish_UsesHigherTfResistance()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);
        var higher = MakeSnapshot(distanceToResist: 0.05m, resistance1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        result.EntryQuality.Should().Be(EntryQuality.Poor);
        result.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void BuildWithHigherTimeframes_Bearish_UsesHigherTfSupport()
    {
        var current = MakeSnapshot(trend: MarketTrend.Bearish, emaBearish: true, isAboveEma200: false,
            isAboveEma20: false, isAboveEma50: false, rsi14: 38m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m, distanceToResist: 0.5m, resistance1Strength: 0.8m,
            distanceToSupport: null, support1Strength: null);
        var higher = MakeSnapshot(distanceToSupport: 0.05m, support1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        result.EntryQuality.Should().Be(EntryQuality.Poor);
        result.RiskFlags.Should().Contain("NearHigherTimeframeSupport");
    }

    [Fact]
    public void BuildWithHigherTimeframes_Neutral_DoesNotSelectLevel()
    {
        var current = MakeSnapshot(trend: MarketTrend.Sideways);
        var higher = MakeSnapshot(distanceToResist: 0.05m, resistance1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        result.EntryQuality.Should().Be(EntryQuality.Poor);
        result.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void BuildWithHigherTimeframes_NegativeDistance_IsIgnored()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);
        var higher = MakeSnapshot(distanceToResist: -0.05m, resistance1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        result.EntryQuality.Should().Be(EntryQuality.Good);
        result.RiskFlags.Should().NotContain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void BuildWithHigherTimeframes_ZeroDistance_IsValid()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);
        var higher = MakeSnapshot(distanceToResist: 0m, resistance1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        result.EntryQuality.Should().Be(EntryQuality.Poor);
        result.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void BuildWithHigherTimeframes_SelectsNearestLevel()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);
        var farther = MakeSnapshot(distanceToResist: 0.5m, resistance1Strength: 0.85m);
        var nearer = MakeSnapshot(distanceToResist: 0.25m, resistance1Strength: 0.85m);

        var result = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, farther, nearer);

        result.EntryQuality.Should().Be(EntryQuality.Fair);
        result.RiskFlags.Should().Contain("NearHigherTimeframeResistance");
    }

    [Fact]
    public void BuildWithHigherTimeframes_NoLevels_PreservesExistingResult()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);

        var expected = TimeframeSummaryBuilder.Build(current, true, MarketRegimes.Trending);
        var actual = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, []);

        actual.EntryQuality.Should().Be(expected.EntryQuality);
        actual.RiskFlags.Should().Equal(expected.RiskFlags);
    }

    [Fact]
    public void BuildWithHigherTimeframes_PreservesEntryQualityAndRiskFlags()
    {
        var current = MakeSnapshot(emaBullish: true, isAboveEma200: true,
            rsi14: 60m, trendStrengthScore: 0.85m, volumeRatio: 1.2m,
            distanceToSupport: 0.5m, support1Strength: 0.8m,
            distanceToResist: null, resistance1Strength: null);
        var higher = MakeSnapshot(distanceToResist: 0.25m, resistance1Strength: 0.85m);

        var expected = TimeframeSummaryBuilder.Build(
            current, true, MarketRegimes.Trending, new NearestOppositeLevel(0.25m, 0.85m));
        var actual = TimeframeSummaryBuilder.BuildWithHigherTimeframes(
            current, true, MarketRegimes.Trending, higher);

        actual.EntryQuality.Should().Be(expected.EntryQuality);
        actual.RiskFlags.Should().Equal(expected.RiskFlags);
    }
}
