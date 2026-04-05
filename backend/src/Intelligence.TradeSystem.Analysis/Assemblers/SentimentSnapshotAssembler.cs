using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="SentimentSnapshot"/> из уже вычисленных снапшотов деривативов,
/// стакана, потока сделок и технического анализа таймфреймов.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входных снапшотов</item>
///   <item>LongShortBiasScore — из LongRatio и ShortRatio снапшота деривативов</item>
///   <item>FundingBiasScore — контрарный скор из ставки финансирования (нормализован через <see cref="FundingNormalizationFactor"/>)</item>
///   <item>OrderBookPressureScore — взвешенное среднее дисбалансов стакана (глубины 5/10/20)</item>
///   <item>TradeFlowPressureScore — нормализованная дельта объёма с корректировкой по флагам агрессии</item>
///   <item>MarketRegime — эвристическая классификация режима по H1 и H4</item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class SentimentSnapshotAssembler
{
    /// <summary>
    /// Нормализующий делитель ставки финансирования.
    /// При <c>|rate| == factor</c> скор достигает ±1.
    /// Совпадает с <see cref="AnalysisThresholds.FundingExtremeThreshold"/>.
    /// </summary>
    private const decimal FundingNormalizationFactor = AnalysisThresholds.FundingExtremeThreshold;

    /// <summary>
    /// Нормализующий делитель дельты объёма.
    /// Дельта 50 % даёт скор ±1; значения выше обрезаются.
    /// </summary>
    private const decimal TradeFlowNormalizationFactor = 50m;

    /// <summary>
    /// Минимальный абсолютный скор, гарантированный при выставленном флаге агрессивного давления.
    /// Если флаг <c>HasAggressiveBuyPressure</c> установлен, скор не будет ниже +<see cref="AggressivePressureFloor"/>.
    /// </summary>
    private const decimal AggressivePressureFloor = 0.5m;

    /// <summary>Вес дисбаланса стакана на глубине 5 в агрегированном скоре.</summary>
    private const decimal ImbalanceWeightTop5 = 0.5m;

    /// <summary>Вес дисбаланса стакана на глубине 10 в агрегированном скоре.</summary>
    private const decimal ImbalanceWeightTop10 = 0.3m;

    /// <summary>Вес дисбаланса стакана на глубине 20 в агрегированном скоре.</summary>
    private const decimal ImbalanceWeightTop20 = 0.2m;

    /// <summary>
    /// Минимальный средний <c>TrendStrengthScore</c> по H1 + H4,
    /// при котором режим классифицируется как <c>Trending</c>.
    /// </summary>
    private const decimal TrendingStrengthThreshold = 0.6m;

    /// <summary>Порог <c>VolumeRatio</c>, при превышении которого фиксируется всплеск объёма (<c>Volatile</c>).</summary>
    private const decimal VolumeSpikeThreshold = 2.0m;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Вычисляет и возвращает <see cref="SentimentSnapshot"/> для переданных рыночных снапшотов.
    /// </summary>
    /// <param name="derivatives">Снапшот деривативных данных: funding rate, long/short ratios.</param>
    /// <param name="orderBook">Снапшот стакана заявок.</param>
    /// <param name="tradeFlow">Снапшот потока совершённых сделок.</param>
    /// <param name="h1">Снапшот технического анализа на таймфрейме 1 ч.</param>
    /// <param name="h4">Снапшот технического анализа на таймфрейме 4 ч.</param>
    /// <exception cref="ArgumentNullException">Если любой из обязательных параметров равен <c>null</c>.</exception>
    public static SentimentSnapshot Assemble(
        DerivativesSnapshot derivatives,
        OrderBookSnapshot orderBook,
        TradeFlowSnapshot tradeFlow,
        TimeframeAnalysisSnapshot h1,
        TimeframeAnalysisSnapshot h4)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(derivatives);
        ArgumentNullException.ThrowIfNull(orderBook);
        ArgumentNullException.ThrowIfNull(tradeFlow);
        ArgumentNullException.ThrowIfNull(h1);
        ArgumentNullException.ThrowIfNull(h4);

        // 2. LongShortBiasScore = LongRatio − ShortRatio
        //    Both ratios in [0, 1] → difference naturally in [−1, 1]; Clamp for safety.
        var longShortBiasScore = Math.Clamp(
            derivatives.LongRatio - derivatives.ShortRatio,
            -1m, 1m);

        // 3. FundingBiasScore — contrarian signal
        //    High positive funding (longs overpaying) → bearish crowd → negative score.
        var fundingBiasScore = ComputeFundingBiasScore(
            derivatives.FundingRate,
            derivatives.FundingRateAvg24h);

        // 4. OrderBookPressureScore — weighted average of pre-computed imbalances
        //    All imbalances are in [−1, 1]; weights sum to 1.0 → result stays in [−1, 1].
        var orderBookPressureScore = Math.Round(
            orderBook.ImbalanceTop5  * ImbalanceWeightTop5  +
            orderBook.ImbalanceTop10 * ImbalanceWeightTop10 +
            orderBook.ImbalanceTop20 * ImbalanceWeightTop20,
            4);

        // 5. TradeFlowPressureScore — normalized delta + aggressive pressure floor
        var tradeFlowPressureScore = ComputeTradeFlowPressureScore(tradeFlow);

        // 6. MarketRegime — heuristic from H1 and H4
        var marketRegime = ClassifyMarketRegime(h1, h4);

        // 7. Assemble
        return new SentimentSnapshot
        {
            LongShortBiasScore     = longShortBiasScore,
            FundingBiasScore       = fundingBiasScore,
            OrderBookPressureScore = orderBookPressureScore,
            TradeFlowPressureScore = tradeFlowPressureScore,
            MarketRegime           = marketRegime,
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Вычисляет контрарный скор настроения из ставки финансирования.
    /// Использует среднее текущей и 24-часовой ставки для сглаживания кратковременных всплесков.
    /// <para>
    /// Формула: <c>−Clamp((rate + avg24h) / 2 / <see cref="FundingNormalizationFactor"/>, −1, 1)</c>
    /// </para>
    /// </summary>
    private static decimal ComputeFundingBiasScore(decimal fundingRate, decimal fundingRateAvg24h)
    {
        // Blend current and 24 h average to smooth intraperiod spikes
        var blended = (fundingRate + fundingRateAvg24h) / 2m;

        // Negate: high positive rate → crowded longs → bearish contrarian signal
        return Math.Round(
            -Math.Clamp(blended / FundingNormalizationFactor, -1m, 1m),
            4);
    }

    /// <summary>
    /// Вычисляет скор давления потока сделок.
    /// <list type="bullet">
    ///   <item>Нормализует <c>DeltaPct</c> в [−1, 1] делением на <see cref="TradeFlowNormalizationFactor"/>.</item>
    ///   <item>Обеспечивает минимальный абсолютный скор <see cref="AggressivePressureFloor"/> при выставленных флагах агрессии.</item>
    /// </list>
    /// </summary>
    private static decimal ComputeTradeFlowPressureScore(TradeFlowSnapshot tradeFlow)
    {
        var score = Math.Clamp(tradeFlow.DeltaPct / TradeFlowNormalizationFactor, -1m, 1m);

        // Aggressive flags guarantee a minimum meaningful signal even when delta is modest
        if (tradeFlow.HasAggressiveBuyPressure && score < AggressivePressureFloor)
            score = AggressivePressureFloor;

        if (tradeFlow.HasAggressiveSellPressure && score > -AggressivePressureFloor)
            score = -AggressivePressureFloor;

        return Math.Round(score, 4);
    }

    /// <summary>
    /// Классифицирует рыночный режим по снапшотам H1 и H4.
    /// <list type="bullet">
    ///   <item><c>Trending</c> — оба таймфрейма согласованы по направлению и сила тренда высокая.</item>
    ///   <item><c>Volatile</c> — направления конфликтуют или зафиксирован всплеск объёма.</item>
    ///   <item><c>MeanReversion</c> — RSI в зоне перегрева на одном из TF или оба в боковике.</item>
    ///   <item><c>Neutral</c> — ни одно из условий выше не выполнено.</item>
    /// </list>
    /// Приоритет классификации: Trending → Volatile → MeanReversion → Neutral.
    /// </summary>
    private static string ClassifyMarketRegime(
        TimeframeAnalysisSnapshot h1,
        TimeframeAnalysisSnapshot h4)
    {
        var avgStrength = (h1.TrendStrengthScore + h4.TrendStrengthScore) / 2m;
        var bothDirectional = h1.Trend == h4.Trend &&
                              (h1.Trend == MarketTrend.Bullish || h1.Trend == MarketTrend.Bearish);

        // Trending: aligned direction + sufficient trend strength
        if (bothDirectional && avgStrength >= TrendingStrengthThreshold)
            return MarketRegimes.Trending;

        // Volatile: opposite direction signals or pronounced volume spike
        var conflicting =
            (h1.Trend == MarketTrend.Bullish && h4.Trend == MarketTrend.Bearish) ||
            (h1.Trend == MarketTrend.Bearish && h4.Trend == MarketTrend.Bullish);

        var volumeSpike = h1.VolumeRatio > VolumeSpikeThreshold ||
                          h4.VolumeRatio > VolumeSpikeThreshold;

        if (conflicting || volumeSpike)
            return MarketRegimes.Volatile;

        // MeanReversion: RSI extreme on any TF, or both stuck in sideways consolidation
        var rsiExtreme = h1.RsiOverbought || h1.RsiOversold ||
                         h4.RsiOverbought || h4.RsiOversold;

        var bothSideways = h1.Trend == MarketTrend.Sideways &&
                           h4.Trend == MarketTrend.Sideways;

        if (rsiExtreme || bothSideways)
            return MarketRegimes.MeanReversion;

        return MarketRegimes.Neutral;
    }

    // ── Regime name constants ────────────────────────────────────────────────

    private static class MarketRegimes
    {
        internal const string Trending      = "Trending";
        internal const string MeanReversion = "MeanReversion";
        internal const string Volatile      = "Volatile";
        internal const string Neutral       = "Neutral";
    }
}

