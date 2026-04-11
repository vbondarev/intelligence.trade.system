namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Единая pure-policy для классификации рыночного режима по агрегированным таймфреймовым снапшотам.
/// Используется как analysis-layer, так и analytics-layer, чтобы исключить рассинхронизацию эвристики.
/// </summary>
public static class MarketRegimePolicy
{
    /// <summary>
    /// Минимальный средний <see cref="TimeframeAnalysisSnapshot.TrendStrengthScore"/> по H1 и H4,
    /// при котором рынок считается направленным.
    /// </summary>
    public const decimal TrendingStrengthThreshold = 0.6m;

    /// <summary>
    /// Порог <see cref="TimeframeAnalysisSnapshot.VolumeRatio"/>, при превышении которого
    /// фиксируется всплеск объёма и режим считается волатильным.
    /// </summary>
    public const decimal VolumeSpikeThreshold = 2.0m;

    /// <summary>
    /// Классифицирует рыночный режим по снапшотам H1 и H4.
    /// Приоритет классификации: Trending → Volatile → MeanReversion → Neutral.
    /// </summary>
    /// <param name="h1">Снапшот технического анализа на таймфрейме 1 час.</param>
    /// <param name="h4">Снапшот технического анализа на таймфрейме 4 часа.</param>
    /// <returns>Одно из канонических значений <see cref="MarketRegimes"/>.</returns>
    /// <exception cref="ArgumentNullException">Если <paramref name="h1"/> или <paramref name="h4"/> равен <c>null</c>.</exception>
    public static string Classify(TimeframeAnalysisSnapshot h1, TimeframeAnalysisSnapshot h4)
    {
        ArgumentNullException.ThrowIfNull(h1);
        ArgumentNullException.ThrowIfNull(h4);

        var avgStrength = (h1.TrendStrengthScore + h4.TrendStrengthScore) / 2m;
        var bothDirectional = h1.Trend == h4.Trend &&
                              (h1.Trend == MarketTrend.Bullish || h1.Trend == MarketTrend.Bearish);

        if (bothDirectional && avgStrength >= TrendingStrengthThreshold)
        {
            return MarketRegimes.Trending;
        }

        var conflicting =
            (h1.Trend == MarketTrend.Bullish && h4.Trend == MarketTrend.Bearish) ||
            (h1.Trend == MarketTrend.Bearish && h4.Trend == MarketTrend.Bullish);

        var volumeSpike = h1.VolumeRatio > VolumeSpikeThreshold ||
                          h4.VolumeRatio > VolumeSpikeThreshold;

        if (conflicting || volumeSpike)
        {
            return MarketRegimes.Volatile;
        }

        var rsiExtreme = h1.RsiOverbought || h1.RsiOversold ||
                         h4.RsiOverbought || h4.RsiOversold;

        var bothSideways = h1.Trend == MarketTrend.Sideways &&
                           h4.Trend == MarketTrend.Sideways;

        if (rsiExtreme || bothSideways)
        {
            return MarketRegimes.MeanReversion;
        }

        return MarketRegimes.Neutral;
    }
}

