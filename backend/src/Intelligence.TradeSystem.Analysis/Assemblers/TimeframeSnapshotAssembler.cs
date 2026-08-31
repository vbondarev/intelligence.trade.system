using Intelligence.TradeSystem.Analysis.Diagnostics;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Calculators;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Levels;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Trend;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Validation;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Оркестрирует полную цепочку вычислений и собирает <see cref="TimeframeAnalysisSnapshot"/>
/// из набора свечей одного таймфрейма.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Сортировка свечей по <c>StartTime ASC</c></item>
///   <item>Проекция в массивы <c>Close[]</c>, <c>High[]</c>, <c>Low[]</c>, <c>Volume[]</c></item>
///   <item>Вычисление индикаторов: EMA 20/50/200, RSI 14, ATR 14, Volume SMA 20</item>
///   <item>Определение уровней поддержки/сопротивления через Volume Profile</item>
///   <item>Классификация тренда и вычисление TrendStrengthScore</item>
///   <item>Сборка производных булевых сигналов и процентных расстояний</item>
///   <item>Сбор диагностики качества индикаторов</item>
/// </list>
/// </para>
/// </summary>
public static class TimeframeSnapshotAssembler
{
    /// <summary>
    /// Вычисляет и возвращает <see cref="TimeframeAssemblyResult"/>, содержащий
    /// <see cref="TimeframeAnalysisSnapshot"/> и диагностику качества индикаторов.
    /// </summary>
    /// <param name="klines">Набор свечей одного символа и одного таймфрейма.</param>
    /// <param name="timeframe">Строковое обозначение таймфрейма: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</param>
    /// <exception cref="ArgumentException">Если <paramref name="klines"/> пустой.</exception>
    /// <exception cref="DataSourceException">Если все свечи не прошли валидацию (проблема качества данных провайдера).</exception>
    public static TimeframeAssemblyResult Assemble(IReadOnlyList<Kline> klines, string timeframe)
    {
        // 1. Normalize
        if (klines.Count == 0)
        {
            throw new ArgumentException("Klines collection is empty.", nameof(klines));
        }

        // 1a. Validate — filter out dirty candles; violations → IndicatorDiagnostics.
        var validKlines = KlineValidator.FilterValid(klines, out var violations);

        if (validKlines.Count == 0)
        {
            throw new DataSourceException(
                "All klines in the collection failed validation. No valid data to assemble a snapshot.");
        }

        var sorted = validKlines.OrderBy(k => k.StartTime).ToArray();

        // 2. Project
        var closes = Array.ConvertAll(sorted, k => k.Close);
        var highs = Array.ConvertAll(sorted, k => k.High);
        var lows = Array.ConvertAll(sorted, k => k.Low);
        var volumes = Array.ConvertAll(sorted, k => k.Volume);

        // 3. Indicators
        var ema20Value = EmaCalculator.Compute(closes, 20);
        var ema50Value = EmaCalculator.Compute(closes, 50);
        var ema200Value = EmaCalculator.Compute(closes, 200);
        var rsi14Value = RsiCalculator.Compute(closes);
        var atr14Value = AtrCalculator.Compute(highs, lows, closes);
        var volSma20Value = SmaCalculator.Compute(volumes, 20);

        // 3a. Diagnostics — собираем в порядке индикаторов (стабильный порядок).
        var indicatorDiagnostics = new List<IndicatorDiagnostic>();

        // Prepend kline-level violations so consumers see data quality issues first.
        foreach (var violation in violations)
        {
            indicatorDiagnostics.Add(new IndicatorDiagnostic
            {
                Timeframe = timeframe,
                Indicator = "kline",
                Reason = IndicatorValueReason.InvalidInput,
                IsFallback = false,
                Message = $"{timeframe}.kline[{violation.KlineIndex}] invalid: {violation.ViolationReason}",
            });
        }

        // Degradation policy — emit additional diagnostics for structurally significant data issues.

        // 1. Last candle by time was filtered out — the most recent market data is absent.
        var originalLatest = klines.Max(k => k.StartTime);
        var validLatest = validKlines.Max(k => k.StartTime);
        if (originalLatest > validLatest)
        {
            indicatorDiagnostics.Add(new IndicatorDiagnostic
            {
                Timeframe = timeframe,
                Indicator = "kline.lastFiltered",
                Reason = IndicatorValueReason.InvalidInput,
                IsFallback = false,
                Message = $"{timeframe}: the most recent candle (StartTime={originalLatest:O}) failed validation " +
                             $"and was excluded. Snapshot reflects data up to {validLatest:O}.",
            });
        }

        // 2. High violation rate — more than KlineHighViolationRateThreshold of input klines were invalid.
        if (violations.Count > 0 &&
            violations.Count / (decimal)klines.Count > AnalysisThresholds.KlineHighViolationRateThreshold)
        {
            indicatorDiagnostics.Add(new IndicatorDiagnostic
            {
                Timeframe = timeframe,
                Indicator = "kline.highViolationRate",
                Reason = IndicatorValueReason.InvalidInput,
                IsFallback = false,
                Message = $"{timeframe}: {violations.Count}/{klines.Count} candles failed validation " +
                             $"({violations.Count * 100 / klines.Count}%), " +
                             $"exceeding the {AnalysisThresholds.KlineHighViolationRateThreshold * 100m:0}% threshold.",
            });
        }

        // 3. Insufficient usable data — valid set is smaller than KlineMinimumUsableCount.
        //    validKlines.Count == 0 already throws above; this handles the 1-candle edge case.
        if (validKlines.Count < AnalysisThresholds.KlineMinimumUsableCount)
        {
            indicatorDiagnostics.Add(new IndicatorDiagnostic
            {
                Timeframe = timeframe,
                Indicator = "kline.insufficientData",
                Reason = IndicatorValueReason.InsufficientData,
                IsFallback = false,
                Message = $"{timeframe}: only {validKlines.Count} valid candle(s) remain after filtering " +
                             $"(minimum usable: {AnalysisThresholds.KlineMinimumUsableCount}). " +
                             "Indicator quality is severely degraded.",
            });
        }

        indicatorDiagnostics.AddIfNeeded(timeframe, "ema20", ema20Value);
        indicatorDiagnostics.AddIfNeeded(timeframe, "ema50", ema50Value);
        indicatorDiagnostics.AddIfNeeded(timeframe, "ema200", ema200Value);
        indicatorDiagnostics.AddIfNeeded(timeframe, "rsi14", rsi14Value);
        indicatorDiagnostics.AddIfNeeded(timeframe, "atr14", atr14Value);
        indicatorDiagnostics.AddIfNeeded(timeframe, "volumeSma20", volSma20Value);

        // Snapshot-модель теперь использует decimal? для EMA/ATR/VolumeSma20/VolumeRatio.
        // Используем .OrNull() — null сигнализирует об отсутствии данных; fake-zero не подставляем.
        var ema20 = ema20Value.OrNull();
        var ema50 = ema50Value.OrNull();
        var ema200 = ema200Value.OrNull();
        var atr14 = atr14Value.OrNull();
        var volSma20 = volSma20Value.OrNull();

        // RSI — snapshot допускает decimal?, поэтому сохраняем null при unavailable.
        var rsi14 = rsi14Value.OrNull();

        var lastVolume = volumes[^1];

        // volumeRatio: считаем только если SMA доступна и > 0; иначе null.
        decimal? volumeRatio = volSma20Value.HasUsableValue() && volSma20Value.RequireValue() > 0m
            ? Math.Round(lastVolume / volSma20Value.RequireValue(), 4)
            : null;

        // volumeRatio diagnostic — emit only when ratio could not be computed.
        // Two cases:
        //   InvalidInput     — SMA is available but == 0 (all volumes are zero); no existing diagnostic covers this.
        //   InsufficientData — SMA itself is unavailable (volumeSma20 diagnostic already exists, but we
        //                      still name the derived indicator explicitly for consumer clarity).
        if (volumeRatio is null)
        {
            var volumeRatioReason = volSma20Value.HasUsableValue()
                ? IndicatorValueReason.InvalidInput
                : IndicatorValueReason.InsufficientData;

            indicatorDiagnostics.Add(new IndicatorDiagnostic
            {
                Timeframe = timeframe,
                Indicator = "volumeRatio",
                Reason = volumeRatioReason,
                IsFallback = false,
                Message = $"{timeframe}.volumeRatio unavailable: {volumeRatioReason}.",
            });
        }

        // 4. Support / Resistance via Volume Profile
        var levels = VolumeProfileDetector.Detect(sorted);

        // 5. Trend
        // TrendClassifier вызывается только если все три EMA доступны.
        // При недоступности — MarketTrend.Unknown и нулевой score.
        var lastClose = closes[^1];
        MarketTrend trend;
        decimal strengthScore;

        if (!ema20Value.HasUsableValue()
            || !ema50Value.HasUsableValue()
            || !ema200Value.HasUsableValue())
        {
            trend = MarketTrend.Unknown;
            strengthScore = 0m;
        }
        else
        {
            (trend, strengthScore) = TrendClassifier.Classify(
                ema20Value.RequireValue(),
                ema50Value.RequireValue(),
                ema200Value.RequireValue(),
                lastClose,
                volumeRatio ?? 0m);   // TrendClassifier expects decimal; null → 0 (no volume boost)
        }

        // 6. Derived signals
        var lastKline = sorted[^1];

        var candleRangePct = lastKline.Close > 0m
            ? Math.Round((lastKline.High - lastKline.Low) / lastKline.Close * 100m, 4)
            : 0m;

        var distToSupport1 = levels.Support1 is { } s1 && lastClose > 0m
            ? Math.Round((lastClose - s1.Price) / lastClose * 100m, 4)
            : (decimal?)null;

        var distToResistance1 = levels.Resistance1 is { } r1 && lastClose > 0m
            ? Math.Round((r1.Price - lastClose) / lastClose * 100m, 4)
            : (decimal?)null;

        // RSI-флаги считаются только при наличии реального значения (Rsi14IsReliable).
        var rsiOverbought = rsi14.HasValue && rsi14.Value >= 70m;
        var rsiOversold = rsi14.HasValue && rsi14.Value <= 30m;

        // EMA boolean flags — false, если EMA недоступна.
        var isAboveEma20 = ema20.HasValue && lastClose > ema20.Value;
        var isAboveEma50 = ema50.HasValue && lastClose > ema50.Value;
        var isAboveEma200 = ema200.HasValue && lastClose > ema200.Value;

        var emaBullishAlignment =
            ema20.HasValue && ema50.HasValue && ema200.HasValue
            && ema20.Value > ema50.Value && ema50.Value > ema200.Value;

        var emaBearishAlignment =
            ema20.HasValue && ema50.HasValue && ema200.HasValue
            && ema20.Value < ema50.Value && ema50.Value < ema200.Value;

        // 7. Assemble
        // Derive indicator availability/fallback flags for consumers (e.g. LlmTimeframeSummaryBuilder).
        var emaIsReliable = ema20Value.HasUsableValue() && ema50Value.HasUsableValue() && ema200Value.HasUsableValue();
        var emaHasFallback = ema20Value.IsFallback || ema50Value.IsFallback || ema200Value.IsFallback;
        var atrIsReliable = atr14Value.IsAvailable;
        var atrIsFallback = atr14Value.IsFallback;
        // VolumeRatioIsReliable must reflect whether the ratio was actually computable (not just SMA availability).
        // If VolumeSma20 == 0 → VolumeRatio is null → not reliable.
        var volumeRatioIsReliable = volumeRatio.HasValue;
        var volumeRatioIsFallback = volumeRatio.HasValue && volSma20Value.IsFallback;

        var snapshot = new TimeframeAnalysisSnapshot
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(DateTime.SpecifyKind(lastKline.StartTime, DateTimeKind.Utc)),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(DateTime.SpecifyKind(lastKline.StartTime, DateTimeKind.Utc)),
                Open = lastKline.Open,
                High = lastKline.High,
                Low = lastKline.Low,
                Close = lastKline.Close,
                Volume = lastKline.Volume,
                Turnover = lastKline.Turnover,
            },

            Ema20 = ema20,
            Ema50 = ema50,
            Ema200 = ema200,
            Rsi14 = rsi14,
            Rsi14IsReliable = rsi14.HasValue,
            Atr14 = atr14,
            VolumeSma20 = volSma20,
            VolumeRatio = volumeRatio,

            TrendStrengthScore = strengthScore,
            Trend = trend,

            Support1 = levels.Support1?.Price,
            Support1Strength = levels.Support1?.Strength,
            Support1ClusterVolume = levels.Support1?.ClusterVolume,
            Support2 = levels.Support2?.Price,
            Support2Strength = levels.Support2?.Strength,
            Support2ClusterVolume = levels.Support2?.ClusterVolume,
            Resistance1 = levels.Resistance1?.Price,
            Resistance1Strength = levels.Resistance1?.Strength,
            Resistance1ClusterVolume = levels.Resistance1?.ClusterVolume,
            Resistance2 = levels.Resistance2?.Price,
            Resistance2Strength = levels.Resistance2?.Strength,
            Resistance2ClusterVolume = levels.Resistance2?.ClusterVolume,

            IsAboveEma20 = isAboveEma20,
            IsAboveEma50 = isAboveEma50,
            IsAboveEma200 = isAboveEma200,

            EmaBullishAlignment = emaBullishAlignment,
            EmaBearishAlignment = emaBearishAlignment,

            RsiOverbought = rsiOverbought,
            RsiOversold = rsiOversold,

            EmaIsReliable = emaIsReliable,
            EmaHasFallback = emaHasFallback,
            AtrIsReliable = atrIsReliable,
            AtrIsFallback = atrIsFallback,
            VolumeRatioIsReliable = volumeRatioIsReliable,
            VolumeRatioIsFallback = volumeRatioIsFallback,

            CandleRangePct = candleRangePct,
            DistanceToSupport1Pct = distToSupport1,
            DistanceToResistance1Pct = distToResistance1,

            IndicatorDiagnostics = [.. indicatorDiagnostics.Select(d => new IndicatorDiagnosticSnapshot
            {
                Timeframe  = d.Timeframe,
                Indicator  = d.Indicator,
                Reason     = d.Reason.ToString(),
                IsFallback = d.IsFallback,
                Message    = d.Message,
            })],
        };

        return new TimeframeAssemblyResult
        {
            Snapshot = snapshot,
            Diagnostics = indicatorDiagnostics,
        };
    }
}
