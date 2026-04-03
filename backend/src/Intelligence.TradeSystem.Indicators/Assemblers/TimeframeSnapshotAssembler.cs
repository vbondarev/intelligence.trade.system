using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Indicators.Calculators;
using Intelligence.TradeSystem.Indicators.Levels;
using Intelligence.TradeSystem.Indicators.Trend;

namespace Intelligence.TradeSystem.Indicators.Assemblers;

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
/// </list>
/// </para>
/// </summary>
public static class TimeframeSnapshotAssembler
{
    /// <summary>
    /// Вычисляет и возвращает <see cref="TimeframeAnalysisSnapshot"/> для переданного набора свечей.
    /// </summary>
    /// <param name="klines">Набор свечей одного символа и одного таймфрейма.</param>
    /// <param name="timeframe">Строковое обозначение таймфрейма: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</param>
    /// <exception cref="ArgumentException">Если <paramref name="klines"/> пустой.</exception>
    public static TimeframeAnalysisSnapshot Assemble(IReadOnlyList<Kline> klines, string timeframe)
    {
        // 1. Normalize
        if (klines.Count == 0)
        {
            throw new ArgumentException("Klines collection is empty.", nameof(klines));
        }

        var sorted = klines.OrderBy(k => k.StartTime).ToArray();

        // 2. Project
        var closes  = Array.ConvertAll(sorted, k => k.Close);
        var highs   = Array.ConvertAll(sorted, k => k.High);
        var lows    = Array.ConvertAll(sorted, k => k.Low);
        var volumes = Array.ConvertAll(sorted, k => k.Volume);

        // 3. Indicators
        var ema20    = EmaCalculator.Compute(closes, 20);
        var ema50    = EmaCalculator.Compute(closes, 50);
        var ema200   = EmaCalculator.Compute(closes, 200);
        var rsi14    = RsiCalculator.Compute(closes);
        var atr14    = AtrCalculator.Compute(highs, lows, closes);
        var volSma20 = SmaCalculator.Compute(volumes, 20);

        var lastVolume  = volumes[^1];
        var volumeRatio = volSma20 > 0m ? Math.Round(lastVolume / volSma20, 4) : 0m;

        // 4. Support / Resistance via Volume Profile
        var levels = VolumeProfileDetector.Detect(sorted);

        // 5. Trend
        var lastClose = closes[^1];
        var (trend, strengthScore) = TrendClassifier.Classify(ema20, ema50, ema200, lastClose, volumeRatio);

        // 6. Derived signals
        var lastKline = sorted[^1];

        var candleRangePct = lastKline.Close > 0m
            ? Math.Round((lastKline.High - lastKline.Low) / lastKline.Close * 100m, 4)
            : 0m;

        var distToSupport1 = levels.Support1 > 0m && lastClose > 0m
            ? Math.Round((lastClose - levels.Support1) / lastClose * 100m, 4)
            : 0m;

        var distToResistance1 = levels.Resistance1 > 0m && lastClose > 0m
            ? Math.Round((levels.Resistance1 - lastClose) / lastClose * 100m, 4)
            : 0m;

        // 7. Assemble
        return new TimeframeAnalysisSnapshot
        {
            Timeframe             = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(DateTime.SpecifyKind(lastKline.StartTime, DateTimeKind.Utc)),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(DateTime.SpecifyKind(lastKline.StartTime, DateTimeKind.Utc)),
                Open        = lastKline.Open,
                High        = lastKline.High,
                Low         = lastKline.Low,
                Close       = lastKline.Close,
                Volume      = lastKline.Volume,
                Turnover    = lastKline.Turnover,
            },

            Ema20    = ema20,
            Ema50    = ema50,
            Ema200   = ema200,
            Rsi14    = rsi14,
            Atr14    = atr14,
            VolumeSma20  = volSma20,
            VolumeRatio  = volumeRatio,

            TrendStrengthScore = strengthScore,
            Trend              = trend,

            Support1    = levels.Support1,
            Support2    = levels.Support2,
            Resistance1 = levels.Resistance1,
            Resistance2 = levels.Resistance2,

            IsAboveEma20  = lastClose > ema20,
            IsAboveEma50  = lastClose > ema50,
            IsAboveEma200 = lastClose > ema200,

            EmaBullishAlignment = ema20 > ema50 && ema50 > ema200,
            EmaBearishAlignment = ema20 < ema50 && ema50 < ema200,

            RsiOverbought = rsi14 >= 70m,
            RsiOversold   = rsi14 <= 30m,

            CandleRangePct           = candleRangePct,
            DistanceToSupport1Pct    = distToSupport1,
            DistanceToResistance1Pct = distToResistance1,
        };
    }
}
