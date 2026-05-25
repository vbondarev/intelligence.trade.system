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
        var s = MakeSnapshot(trend: MarketTrend.Bullish, trendStrengthScore: 0.6m, volumeRatio: 0.4m);
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
        MarketTrend trend = MarketTrend.Bullish,
        bool emaBullish = false,
        bool emaBearish = false,
        bool isAboveEma200 = true,
        decimal? rsi14 = 55m,
        bool rsiOverbought = false,
        bool rsiOversold = false,
        decimal trendStrengthScore = 0.6m,
        decimal volumeRatio = 1.1m,
        decimal? distanceToSupport = 0.6m,
        decimal? distanceToResist = 0.3m,
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
            Support2 = 97m,
            Resistance1 = 106m,
            Resistance2 = 108m,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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
            rsi14: 38m, trendStrengthScore: 0.85m,
            volumeRatio: 1.2m,
            distanceToResist: 0.5m,
            rsi14IsReliable: true, emaIsReliable: true, atrIsReliable: true, volumeRatioIsReliable: true);

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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

        var r = LlmTimeframeSummaryBuilder.Build(s);

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
}
