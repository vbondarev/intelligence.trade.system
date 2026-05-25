using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Indicators.Results;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

/// <summary>
/// Integration tests для полного pipeline:
/// klines → <see cref="TimeframeSnapshotAssembler"/> → <see cref="TimeframeAnalysisSnapshot"/>.
///
/// Защищают от регрессий в поведении nullable-индикаторов:
/// - unavailable → null в snapshot, не fake-zero;
/// - fallback → числовое значение + diagnostic;
/// - boolean flags корректны при null EMA/RSI.
/// </summary>
public sealed class IndicatorPipelineIntegrationTests
{
    // ─── Scenario 1: RSI unavailable serializes as null ──────────────────────

    [Fact]
    public void Pipeline_Sets_Rsi14_To_Null_And_Adds_Diagnostic_When_Insufficient_Candles()
    {
        // RSI14 requires period + 1 = 15 candles. 5 candles → Unavailable.
        var klines = KlineFactory.CreateSeries(count: 5);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");
        var s = result.Snapshot;

        // Null, not 0.
        s.Rsi14.Should().BeNull(because: "rsi14 must be null when data is insufficient, not 0");
        s.Rsi14IsReliable.Should().BeFalse();
        s.RsiOverbought.Should().BeFalse(because: "unavailable RSI must not trigger overbought");
        s.RsiOversold.Should().BeFalse(because: "unavailable RSI must not trigger oversold");

        // Diagnostic explains the null.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "rsi14").Subject;
        diag.Timeframe.Should().Be("15m");
        diag.Reason.Should().Be(IndicatorValueReason.InsufficientData.ToString());
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("rsi14");
        diag.Message.Should().Contain("unavailable");
    }

    // ─── Scenario 2: ATR unavailable serializes as null ──────────────────────

    [Fact]
    public void Pipeline_Sets_Atr14_To_Null_And_Adds_Diagnostic_When_Only_One_Candle()
    {
        // AtrCalculator requires >= 2 candles. 1 candle → Unavailable.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "4h");
        var s = result.Snapshot;

        s.Atr14.Should().BeNull(because: "atr14 must be null when only 1 candle, not 0");
        s.AtrIsReliable.Should().BeFalse();

        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "atr14").Subject;
        diag.Timeframe.Should().Be("4h");
        diag.Reason.Should().Be(IndicatorValueReason.InsufficientData.ToString());
        diag.IsFallback.Should().BeFalse();
    }

    // ─── Scenario 3: EMA200 partial window keeps value but adds fallback diag ─

    [Fact]
    public void Pipeline_Keeps_Ema200_Value_And_Adds_FallbackDiagnostic_When_Partial_Window()
    {
        // EMA200 with only 50 candles → Fallback(PartialWindow).
        var klines = KlineFactory.CreateSeries(count: 50);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");
        var s = result.Snapshot;

        // Fallback value is not null — it's a real (though partial) estimate.
        s.Ema200.Should().NotBeNull(because: "EMA200 computes a fallback with partial window");
        s.Ema200.Should().BeGreaterThan(0m);
        s.EmaHasFallback.Should().BeTrue();

        // Diagnostic reports the partial window.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "ema200").Subject;
        diag.Reason.Should().Be(IndicatorValueReason.PartialWindow.ToString());
        diag.IsFallback.Should().BeTrue();
        diag.Message.Should().Contain("fallback");
    }

    // ─── Scenario 4: VolumeSma20 partial window adds fallback diagnostic ──────

    [Fact]
    public void Pipeline_Adds_FallbackDiagnostic_When_VolumeSma20_Uses_Partial_Window()
    {
        // SmaCalculator with 10 candles and period 20 → Fallback(PartialWindow).
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        // VolumeSma20 has a fallback value (average of available volumes).
        s.VolumeSma20.Should().NotBeNull();
        s.VolumeSma20.Should().BeGreaterThan(0m);
        s.VolumeRatioIsFallback.Should().BeTrue();

        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "volumeSma20").Subject;
        diag.Reason.Should().Be(IndicatorValueReason.PartialWindow.ToString());
        diag.IsFallback.Should().BeTrue();
    }

    // ─── Scenario 5: EMA alignment is false when EMA has zero-fake risk ──────

    [Fact]
    public void Pipeline_Boolean_EmaFlags_Reflect_Real_Values_Not_FakeZero()
    {
        // With a small set of candles all EMAs are computed (fallback), but from the same prices.
        // When EMA20 == EMA50 == EMA200 (same single price seed), alignment must be false.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        // All EMAs have values (fallback = price itself for 1 candle).
        s.Ema20.Should().NotBeNull();
        s.Ema50.Should().NotBeNull();
        s.Ema200.Should().NotBeNull();

        // With equal EMAs (all = same seed price), neither alignment is true.
        s.EmaBullishAlignment.Should().BeFalse(
            because: "EMA20 == EMA50 == EMA200 (same seed price) cannot produce bullish alignment");
        s.EmaBearishAlignment.Should().BeFalse(
            because: "EMA20 == EMA50 == EMA200 (same seed price) cannot produce bearish alignment");

        // IsAbove flags reflect real comparison, not fake-zero.
        var expectedAbove20 = s.Ema20.HasValue && s.LastCandle.Close > s.Ema20.Value;
        var expectedAbove200 = s.Ema200.HasValue && s.LastCandle.Close > s.Ema200.Value;
        s.IsAboveEma20.Should().Be(expectedAbove20);
        s.IsAboveEma200.Should().Be(expectedAbove200);
    }

    // ─── Scenario 6: RSI unavailable does not create false oversold ──────────

    [Fact]
    public void Pipeline_Does_Not_Mark_RsiOversold_When_Rsi_Is_Unavailable()
    {
        // Bearish series with very few candles → RSI unavailable.
        var klines = KlineFactory.CreateSeries(count: 5, trend: SeriesTrend.Bearish, startPrice: 200m);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        s.Rsi14.Should().BeNull();
        s.RsiOversold.Should().BeFalse(because: "null RSI must never trigger oversold (fake-zero protection)");
        s.RsiOverbought.Should().BeFalse();
    }

    // ─── Scenario 7: No diagnostics with sufficient data ─────────────────────

    [Fact]
    public void Pipeline_Produces_No_Diagnostics_When_All_Indicators_Fully_Available()
    {
        // 250 candles → EMA20/50/200, RSI14, ATR14, VolumeSma20 all fully available.
        // KlineFactory produces candles with non-zero volume → VolumeRatio is also computable.
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        s.IndicatorDiagnostics.Should().BeEmpty(
            because: "250 candles with non-zero volume is sufficient for all indicators — no diagnostics expected");

        // All indicator values are non-null.
        s.Ema20.Should().NotBeNull();
        s.Ema50.Should().NotBeNull();
        s.Ema200.Should().NotBeNull();
        s.Rsi14.Should().NotBeNull();
        s.Atr14.Should().NotBeNull();
        s.VolumeSma20.Should().NotBeNull();
        s.VolumeRatio.Should().NotBeNull(because: "non-zero volumes → VolumeRatio is computable");
        s.Rsi14IsReliable.Should().BeTrue();
        s.EmaIsReliable.Should().BeTrue();
        s.AtrIsReliable.Should().BeTrue();
        s.VolumeRatioIsReliable.Should().BeTrue();
    }

    // ─── Scenario 8: Diagnostics stable order ────────────────────────────────

    [Fact]
    public void Pipeline_Diagnostics_Are_In_Stable_Indicator_Order_Within_Timeframe()
    {
        // 10 candles → ema20/50 may be partial or available, ema200 partial,
        // rsi14 unavailable, atr14 available, volumeSma20 partial.
        // Volumes are non-zero → volumeRatio IS computable (no volumeRatio diagnostic).
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // Extract indicator names in the order they appear.
        var indicatorOrder = result.Snapshot.IndicatorDiagnostics.Select(d => d.Indicator).ToList();

        // Expected stable order: ema20 → ema50 → ema200 → rsi14 → atr14 → volumeSma20 → volumeRatio.
        // volumeRatio appears only when VolumeRatio is null; in this scenario it is computable → not present.
        var expectedOrder = new[] { "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeSma20", "volumeRatio" };
        var presentInOrder = expectedOrder.Where(indicatorOrder.Contains).ToList();

        indicatorOrder.Should().ContainInOrder(presentInOrder,
            because: "indicator diagnostics must be emitted in the canonical stable order");
    }

    // ─── Scenario 8b: volumeRatio diagnostic comes after volumeSma20 ─────────

    [Fact]
    public void Pipeline_VolumeRatio_Diagnostic_Comes_After_VolumeSma20_In_Stable_Order()
    {
        // All volumes = 0 → VolumeSma20 = Available(0) → VolumeRatio = null → volumeRatio diagnostic emitted.
        // VolumeSma20 is Available(0) → no volumeSma20 diagnostic.
        // Stable order must place volumeRatio after volumeSma20 (even when volumeSma20 diagnostic is absent).
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = Enumerable.Range(0, 25)
            .Select(i => KlineFactory.Create(volume: 0m, startTime: baseTime.AddHours(i)))
            .ToList();

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var indicatorOrder = result.Snapshot.IndicatorDiagnostics.Select(d => d.Indicator).ToList();

        // volumeRatio must be present and must appear after any earlier indicators.
        indicatorOrder.Should().Contain("volumeRatio");
        indicatorOrder.Should().NotContain("volumeSma20",
            because: "VolumeSma20 = Available(0) → not a fallback → no volumeSma20 diagnostic");

        // No other indicator (ema/rsi/atr) appears after volumeRatio.
        var volumeRatioIdx = indicatorOrder.IndexOf("volumeRatio");
        var laterIndicators = indicatorOrder.Skip(volumeRatioIdx + 1).ToList();
        var priorIndicators = new[] { "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeSma20" };
        laterIndicators.Should().NotContain(priorIndicators,
            because: "volumeRatio must be the last diagnostic in stable order");
    }

    // ─── Scenario 9: Assembler diagnostic count across multiple timeframes ───

    [Fact]
    public void MarketAnalysisSnapshotAssembler_Aggregates_Diagnostics_From_All_Timeframes()
    {
        // Build snapshots with different data amounts to trigger different diagnostics.
        var m15 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 5), "15m").Snapshot;
        var h1 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 10), "1h").Snapshot;
        var h4 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 250), "4h").Snapshot;
        var d1 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 50), "1d").Snapshot;

        // Build a market snapshot (minimal required data for non-timeframe fields).
        var (_, allDiags) = BuildMinimalMarketSnapshot(m15, h1, h4, d1);

        allDiags.Should().Contain(d => d.Timeframe == "15m");
        allDiags.Should().Contain(d => d.Timeframe == "1h");
        allDiags.Should().Contain(d => d.Timeframe == "1d");

        // 4h with 250 candles should not add any diagnostics.
        allDiags.Should().NotContain(d => d.Timeframe == "4h");

        // Order: 15m first, then 1h, 4h, 1d.
        var timeframes = allDiags.Select(d => d.Timeframe).Distinct().ToList();
        var orderedExpected = _timeframeOrder.Where(tf => timeframes.Contains(tf)).ToList();
        timeframes.Should().ContainInOrder(orderedExpected,
            because: "diagnostics are aggregated in timeframe order: 15m → 1h → 4h → 1d");
    }

    // ─── Scenario 10: VolumeRatio diagnostic when VolumeSma20 is zero ────────

    [Fact]
    public void Pipeline_VolumeRatio_Is_Null_When_All_Candle_Volumes_Are_Zero()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = Enumerable.Range(0, 25)
            .Select(i => KlineFactory.Create(volume: 0m, startTime: baseTime.AddHours(i)))
            .ToList();

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        s.VolumeRatio.Should().BeNull(
            because: "VolumeSma20 = 0 → VolumeRatio cannot be computed → null, not fake-zero");
        s.VolumeRatioIsReliable.Should().BeFalse();

        // Diagnostic must explain why VolumeRatio is absent.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "volumeRatio").Subject;
        diag.Timeframe.Should().Be("1h");
        diag.Reason.Should().Be(IndicatorValueReason.InvalidInput.ToString(),
            because: "VolumeSma20 is Available(0) — division by zero is the cause, not missing data");
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("volumeRatio").And.Contain("unavailable");
    }

    private static readonly string[] _timeframeOrder = ["15m", "1h", "1d"];

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds minimal market snapshot and returns its aggregated diagnostics,
    /// bypassing real exchange data by calling MarketAnalysisSnapshotAssembler directly.
    /// </summary>
    private static (MarketAnalysisSnapshot Snapshot, IReadOnlyList<IndicatorDiagnosticSnapshot> Diagnostics)
        BuildMinimalMarketSnapshot(
            TimeframeAnalysisSnapshot m15,
            TimeframeAnalysisSnapshot h1,
            TimeframeAnalysisSnapshot h4,
            TimeframeAnalysisSnapshot d1)
    {
        var snapshot = MarketAnalysisSnapshotAssembler.Assemble(
            exchange: "Bybit",
            symbol: "BTCUSDT",
            category: MarketCategory.Linear,
            price: TestSnapshotFactory.CreatePrice(),
            derivatives: TestSnapshotFactory.CreateDerivatives(),
            orderBook: TestSnapshotFactory.CreateOrderBook(),
            tradeFlow: TestSnapshotFactory.CreateTradeFlow(),
            m15: m15, h1: h1, h4: h4, d1: d1,
            sentiment: TestSnapshotFactory.CreateSentiment(),
            portfolio: TestSnapshotFactory.CreatePortfolio());

        return (snapshot, snapshot.IndicatorDiagnostics);
    }
}
