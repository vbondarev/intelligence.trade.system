using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;
using Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;
using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Assemblers;

/// <summary>
/// Интеграционные тесты полного конвейера:
/// klines → <see cref="TimeframeSnapshotAssembler"/> → <see cref="TimeframeAnalysisSnapshot"/>.
///
/// Защищают от регрессий в поведении nullable-индикаторов:
/// - unavailable → null в snapshot, не fake-zero;
/// - fallback → числовое значение + diagnostic;
/// - логические флаги корректны при null EMA/RSI.
/// </summary>
public sealed class IndicatorPipelineIntegrationTests
{
    // ─── Сценарий 1: недоступный RSI сериализуется как null ──────────

    [Fact]
    public void Pipeline_Sets_Rsi14_To_Null_And_Adds_Diagnostic_When_Insufficient_Candles()
    {
        // RSI14 требует period + 1 = 15 свечей. 5 свечей → Unavailable.
        var klines = KlineFactory.CreateSeries(count: 5);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");
        var s = result.Snapshot;

        // Null, а не 0.
        s.Rsi14.Should().BeNull(because: "rsi14 must be null when data is insufficient, not 0");
        s.Rsi14IsReliable.Should().BeFalse();
        s.RsiOverbought.Should().BeFalse(because: "unavailable RSI must not trigger overbought");
        s.RsiOversold.Should().BeFalse(because: "unavailable RSI must not trigger oversold");

        // Diagnostic объясняет значение null.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "rsi14").Subject;
        diag.Timeframe.Should().Be("15m");
        diag.Reason.Should().Be(IndicatorValueReason.InsufficientData.ToString());
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("rsi14");
        diag.Message.Should().Contain("unavailable");
    }

    // ─── Сценарий 2: недоступный ATR сериализуется как null ──────────

    [Fact]
    public void Pipeline_Sets_Atr14_To_Null_And_Adds_Diagnostic_When_Only_One_Candle()
    {
        // AtrCalculator требует >= 2 свечей. 1 свеча → Unavailable.
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

    // ─── Сценарий 3: частичное окно EMA200 сохраняет значение и добавляет fallback diag ─

    [Fact]
    public void Pipeline_Keeps_Ema200_Value_And_Adds_FallbackDiagnostic_When_Partial_Window()
    {
        // EMA200 только с 50 свечами → Fallback(PartialWindow).
        var klines = KlineFactory.CreateSeries(count: 50);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "15m");
        var s = result.Snapshot;

        // Fallback-значение не null — это реальная, хотя и частичная, оценка.
        s.Ema200.Should().NotBeNull(because: "EMA200 computes a fallback with partial window");
        s.Ema200.Should().BeGreaterThan(0m);
        s.EmaHasFallback.Should().BeTrue();

        // Diagnostic сообщает о частичном окне.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "ema200").Subject;
        diag.Reason.Should().Be(IndicatorValueReason.PartialWindow.ToString());
        diag.IsFallback.Should().BeTrue();
        diag.Message.Should().Contain("fallback");
    }

    // ─── Сценарий 4: частичное окно VolumeSma20 добавляет fallback diagnostic ─

    [Fact]
    public void Pipeline_Adds_FallbackDiagnostic_When_VolumeSma20_Uses_Partial_Window()
    {
        // SmaCalculator с 10 свечами и period=20 → Fallback(PartialWindow).
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        // VolumeSma20 содержит fallback-значение — среднее по доступным объёмам.
        s.VolumeSma20.Should().NotBeNull();
        s.VolumeSma20.Should().BeGreaterThan(0m);
        s.VolumeRatioIsFallback.Should().BeTrue();

        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "volumeSma20").Subject;
        diag.Reason.Should().Be(IndicatorValueReason.PartialWindow.ToString());
        diag.IsFallback.Should().BeTrue();
    }

    // ─── Сценарий 5: EMA alignment равен false при риске фиктивного нулевого EMA ─

    [Fact]
    public void Pipeline_Boolean_EmaFlags_Reflect_Real_Values_Not_FakeZero()
    {
        // При небольшом наборе свечей все EMA вычисляются (fallback), используя одни и те же цены.
        // При EMA20 == EMA50 == EMA200 (одинаковом seed из одной цены) alignment должен быть false.
        var klines = KlineFactory.CreateSeries(count: 1);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        // Все EMA имеют значения (fallback для одной свечи равен самой цене).
        s.Ema20.Should().NotBeNull();
        s.Ema50.Should().NotBeNull();
        s.Ema200.Should().NotBeNull();

        // При равных EMA (все равны одной seed-цене) ни один alignment не равен true.
        s.EmaBullishAlignment.Should().BeFalse(
            because: "EMA20 == EMA50 == EMA200 (same seed price) cannot produce bullish alignment");
        s.EmaBearishAlignment.Should().BeFalse(
            because: "EMA20 == EMA50 == EMA200 (same seed price) cannot produce bearish alignment");

        // Flags IsAbove отражают реальное сравнение, а не fake-zero.
        var expectedAbove20 = s.Ema20.HasValue && s.LastCandle.Close > s.Ema20.Value;
        var expectedAbove200 = s.Ema200.HasValue && s.LastCandle.Close > s.Ema200.Value;
        s.IsAboveEma20.Should().Be(expectedAbove20);
        s.IsAboveEma200.Should().Be(expectedAbove200);
    }

    // ─── Сценарий 6: недоступный RSI не создаёт ложный oversold ────────

    [Fact]
    public void Pipeline_Does_Not_Mark_RsiOversold_When_Rsi_Is_Unavailable()
    {
        // В bearish-серии слишком мало свечей → RSI недоступен.
        var klines = KlineFactory.CreateSeries(count: 5, trend: SeriesTrend.Bearish, startPrice: 200m);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        s.Rsi14.Should().BeNull();
        s.RsiOversold.Should().BeFalse(because: "null RSI must never trigger oversold (fake-zero protection)");
        s.RsiOverbought.Should().BeFalse();
    }

    // ─── Сценарий 7: diagnostics отсутствуют при достаточном объёме данных ───

    [Fact]
    public void Pipeline_Produces_No_Diagnostics_When_All_Indicators_Fully_Available()
    {
        // 250 свечей → EMA20/50/200, RSI14, ATR14 и VolumeSma20 полностью доступны.
        // KlineFactory создаёт свечи с ненулевым объёмом → VolumeRatio также можно вычислить.
        var klines = KlineFactory.CreateSeries(count: 250);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var s = result.Snapshot;

        s.IndicatorDiagnostics.Should().BeEmpty(
            because: "250 candles with non-zero volume is sufficient for all indicators — no diagnostics expected");

        // Все значения индикаторов не null.
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

    // ─── Сценарий 8: стабильный порядок diagnostics ───────────────────

    [Fact]
    public void Pipeline_Diagnostics_Are_In_Stable_Indicator_Order_Within_Timeframe()
    {
        // 10 свечей → ema20/50 могут быть partial или available, ema200 — partial,
        // rsi14 — unavailable, atr14 — available, volumeSma20 — partial.
        // Объёмы ненулевые → volumeRatio вычисляется, diagnostic для volumeRatio отсутствует.
        var klines = KlineFactory.CreateSeries(count: 10);
        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");

        // Извлекаем имена индикаторов в порядке появления.
        var indicatorOrder = result.Snapshot.IndicatorDiagnostics.Select(d => d.Indicator).ToList();

        // Ожидаемый стабильный порядок: ema20 → ema50 → ema200 → rsi14 → atr14 → volumeSma20 → volumeRatio.
        // diagnostic volumeRatio появляется, когда значение VolumeRatio равно null; в этом сценарии ratio вычисляется → diagnostic отсутствует.
        var expectedOrder = new[] { "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeSma20", "volumeRatio" };
        var presentInOrder = expectedOrder.Where(indicatorOrder.Contains).ToList();

        indicatorOrder.Should().ContainInOrder(presentInOrder,
            because: "indicator diagnostics must be emitted in the canonical stable order");
    }

    // ─── Сценарий 8b: diagnostic volumeRatio идёт после volumeSma20 ────

    [Fact]
    public void Pipeline_VolumeRatio_Diagnostic_Comes_After_VolumeSma20_In_Stable_Order()
    {
        // Все объёмы равны 0 → VolumeSma20 = Available(0) → VolumeRatio = null → добавляется diagnostic volumeRatio.
        // VolumeSma20 = Available(0) → diagnostic volumeSma20 не добавляется.
        // Стабильный порядок должен располагать volumeRatio после volumeSma20, даже если diagnostic volumeSma20 отсутствует.
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var klines = Enumerable.Range(0, 25)
            .Select(i => KlineFactory.Create(volume: 0m, startTime: baseTime.AddHours(i)))
            .ToList();

        var result = TimeframeSnapshotAssembler.Assemble(klines, timeframe: "1h");
        var indicatorOrder = result.Snapshot.IndicatorDiagnostics.Select(d => d.Indicator).ToList();

        // Diagnostic volumeRatio присутствует, поэтому должен идти после всех предыдущих индикаторов.
        indicatorOrder.Should().Contain("volumeRatio");
        indicatorOrder.Should().NotContain("volumeSma20",
            because: "VolumeSma20 = Available(0) → not a fallback → no volumeSma20 diagnostic");

        // После volumeRatio другие индикаторы (ema/rsi/atr) отсутствуют.
        var volumeRatioIdx = indicatorOrder.IndexOf("volumeRatio");
        var laterIndicators = indicatorOrder.Skip(volumeRatioIdx + 1).ToList();
        var priorIndicators = new[] { "ema20", "ema50", "ema200", "rsi14", "atr14", "volumeSma20" };
        laterIndicators.Should().NotContain(priorIndicators,
            because: "volumeRatio must be the last diagnostic in stable order");
    }

    // ─── Сценарий 9: количество diagnostics assembler по нескольким таймфреймам ─

    [Fact]
    public void MarketSnapshotAssembler_Aggregates_Diagnostics_From_All_Timeframes()
    {
        // Собрать snapshots с разным объёмом данных, чтобы вызвать разные diagnostics.
        var m15 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 5), "15m").Snapshot;
        var h1 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 10), "1h").Snapshot;
        var h4 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 250), "4h").Snapshot;
        var d1 = TimeframeSnapshotAssembler.Assemble(KlineFactory.CreateSeries(count: 50), "1d").Snapshot;

        // Собрать market snapshot (минимальные обязательные данные для полей вне timeframe).
        var (_, allDiags) = BuildMinimalMarketSnapshot(m15, h1, h4, d1);

        allDiags.Should().Contain(d => d.Timeframe == "15m");
        allDiags.Should().Contain(d => d.Timeframe == "1h");
        allDiags.Should().Contain(d => d.Timeframe == "1d");

        // Для 4h с 250 свечами diagnostics добавляться не должны.
        allDiags.Should().NotContain(d => d.Timeframe == "4h");

        // Порядок: сначала 15m, затем 1h, 4h, 1d.
        var timeframes = allDiags.Select(d => d.Timeframe).Distinct().ToList();
        var orderedExpected = _timeframeOrder.Where(tf => timeframes.Contains(tf)).ToList();
        timeframes.Should().ContainInOrder(orderedExpected,
            because: "diagnostics are aggregated in timeframe order: 15m → 1h → 4h → 1d");
    }

    // ─── Сценарий 10: diagnostic VolumeRatio при нулевом VolumeSma20 ────────

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

        // Diagnostic должен объяснять, почему VolumeRatio отсутствует.
        var diag = s.IndicatorDiagnostics.Should().ContainSingle(d => d.Indicator == "volumeRatio").Subject;
        diag.Timeframe.Should().Be("1h");
        diag.Reason.Should().Be(IndicatorValueReason.InvalidInput.ToString(),
            because: "VolumeSma20 is Available(0) — division by zero is the cause, not missing data");
        diag.IsFallback.Should().BeFalse();
        diag.Message.Should().Contain("volumeRatio").And.Contain("unavailable");
    }

    private static readonly string[] _timeframeOrder = ["15m", "1h", "1d"];

    // ─── Вспомогательные методы ───────────────────────────────────────────────

    /// <summary>
    /// Строит минимальный рыночный снимок и возвращает его агрегированные диагностические данные,
    /// обходя реальные биржевые данные прямым вызовом MarketSnapshotAssembler.
    /// </summary>
    private static (MarketSnapshot Snapshot, IReadOnlyList<IndicatorDiagnosticSnapshot> Diagnostics)
        BuildMinimalMarketSnapshot(
            TimeframeAnalysisSnapshot m15,
            TimeframeAnalysisSnapshot h1,
            TimeframeAnalysisSnapshot h4,
            TimeframeAnalysisSnapshot d1)
    {
        var snapshot = MarketSnapshotAssembler.Assemble(
            exchange: "Bybit",
            symbol: "BTCUSDT",
            category: MarketCategory.Linear,
            price: TestSnapshotFactory.CreatePrice(),
            derivatives: TestSnapshotFactory.CreateDerivatives(),
            orderBook: TestSnapshotFactory.CreateOrderBook(),
            tradeFlow: TestSnapshotFactory.CreateTradeFlow(),
            m15: m15, h1: h1, h4: h4, d1: d1,
            sentiment: TestSnapshotFactory.CreateSentiment());

        return (snapshot, snapshot.IndicatorDiagnostics);
    }
}
