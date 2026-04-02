using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Indicators.Trend;

/// <summary>
/// Определяет направление рыночного тренда и его силу на основе выравнивания EMA.
/// </summary>
internal static class TrendClassifier
{
    /// <summary>
    /// Возвращает направление тренда и нормализованную оценку его силы [0, 1].
    /// <para>
    /// Логика классификации:
    /// <list type="bullet">
    ///   <item><c>EMA20 &gt; EMA50 &gt; EMA200</c> → <see cref="MarketTrend.Bullish"/></item>
    ///   <item><c>EMA20 &lt; EMA50 &lt; EMA200</c> → <see cref="MarketTrend.Bearish"/></item>
    ///   <item>Иначе → <see cref="MarketTrend.Sideways"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// <c>TrendStrengthScore</c> складывается из трёх условий выравнивания EMA (по 0.33 каждое)
    /// и буста от объёма до +0.2 при <c>VolumeRatio &gt; 1</c>.
    /// </para>
    /// </summary>
    public static (MarketTrend Trend, decimal StrengthScore) Classify(
        decimal ema20,
        decimal ema50,
        decimal ema200,
        decimal currentPrice,
        decimal volumeRatio)
    {
        var bullishAlignment = ema20 > ema50 && ema50 > ema200;
        var bearishAlignment = ema20 < ema50 && ema50 < ema200;

        // Базовый score: каждое из трёх условий выравнивания даёт 0.33
        decimal baseScore;

        if (bullishAlignment || bearishAlignment)
        {
            baseScore = 1m;
        }
        else
        {
            var points = 0m;

            if (ema20  > ema50)   points += 0.33m;
            if (ema50  > ema200)  points += 0.33m;
            if (currentPrice > ema200) points += 0.34m;

            // При медвежьем частичном выравнивании берём обратные условия
            var bearPoints = 0m;
            if (ema20  < ema50)   bearPoints += 0.33m;
            if (ema50  < ema200)  bearPoints += 0.33m;
            if (currentPrice < ema200) bearPoints += 0.34m;

            baseScore = Math.Max(points, bearPoints);
        }

        // Буст от объёма: до +0.2 при VolumeRatio > 1
        var volumeBoost = volumeRatio > 1m
            ? Math.Min((volumeRatio - 1m) * 0.1m, 0.2m)
            : 0m;

        var strengthScore = Math.Min(baseScore + volumeBoost, 1m);

        var trend = bullishAlignment ? MarketTrend.Bullish
                  : bearishAlignment ? MarketTrend.Bearish
                  : MarketTrend.Sideways;

        return (trend, Math.Round(strengthScore, 4));
    }
}

