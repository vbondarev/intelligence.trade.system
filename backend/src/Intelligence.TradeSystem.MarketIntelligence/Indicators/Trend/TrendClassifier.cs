using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Indicators.Trend;

/// <summary>
/// Определяет направление рыночного тренда и его силу на основе взаимного расположения
/// экспоненциальных скользящих средних и текущей цены.
/// </summary>
/// <remarks>
/// Логика классификации:
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="MarketTrend.Bullish"/> — если <c>EMA20 &gt; EMA50 &gt; EMA200</c>
/// и текущая цена выше <c>EMA200</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="MarketTrend.Bearish"/> — если <c>EMA20 &lt; EMA50 &lt; EMA200</c>
/// и текущая цена ниже <c>EMA200</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Во всех остальных случаях возвращается <see cref="MarketTrend.Sideways"/>.
/// </description>
/// </item>
/// </list>
/// <para>
/// Сила тренда возвращается в диапазоне <c>[0; 1]</c>.
/// Для направленного тренда базовая сила равна <c>0.80</c>.
/// Повышенный объём может увеличить её до <c>1.00</c>, делая подтверждённый тренд
/// сильнее, чем тренд без объёмной поддержки.
/// Для бокового рынка рассчитывается частичная структурная оценка, но ограничивается сверху
/// значением <c>0.49</c>, чтобы состояние <c>Sideways</c> не выглядело как сильный тренд.
/// </para>
/// <para>
/// Дополнительное усиление за объём применяется только для направленного тренда
/// и только если <c>volumeRatio</c> больше <c>1</c>.
/// </para>
/// </remarks>
public static class TrendClassifier
{
    private const decimal DirectedTrendBaseScore = 0.80m;
    private const decimal MaxVolumeBoost = 0.20m;

    /// <summary>
    /// Классифицирует направление тренда и возвращает оценку его силы.
    /// </summary>
    /// <param name="ema20">Значение EMA с периодом 20.</param>
    /// <param name="ema50">Значение EMA с периодом 50.</param>
    /// <param name="ema200">Значение EMA с периодом 200.</param>
    /// <param name="currentPrice">Текущая цена инструмента.</param>
    /// <param name="volumeRatio">
    /// Отношение текущего объёма к среднему объёму.
    /// Значение больше <c>1</c> интерпретируется как повышенный объём.
    /// </param>
    /// <returns>
    /// Кортеж из:
    /// <list type="bullet">
    /// <item><description><c>Trend</c> — направление рынка.</description></item>
    /// <item><description><c>StrengthScore</c> — сила тренда в диапазоне <c>[0; 1]</c>.
    /// Для <c>Bullish</c>/<c>Bearish</c> — от <c>0.80</c> до <c>1.00</c>,
    /// для <c>Sideways</c> — от <c>0.00</c> до <c>0.49</c>.</description></item>
    /// </list>
    /// </returns>
    public static (MarketTrend Trend, decimal StrengthScore) Classify(
        decimal ema20,
        decimal ema50,
        decimal ema200,
        decimal currentPrice,
        decimal volumeRatio)
    {
        volumeRatio = Math.Max(0m, volumeRatio);

        var bullishAlignment = ema20 > ema50 && ema50 > ema200;
        var bearishAlignment = ema20 < ema50 && ema50 < ema200;

        var isPriceAboveEma200 = currentPrice > ema200;
        var isPriceBelowEma200 = currentPrice < ema200;

        MarketTrend trend;
        decimal baseScore;

        if (bullishAlignment && isPriceAboveEma200)
        {
            trend = MarketTrend.Bullish;
            baseScore = DirectedTrendBaseScore;
        }
        else if (bearishAlignment && isPriceBelowEma200)
        {
            trend = MarketTrend.Bearish;
            baseScore = DirectedTrendBaseScore;
        }
        else
        {
            trend = MarketTrend.Sideways;

            var bullPoints = 0m;
            if (ema20 > ema50)
            {
                bullPoints += 0.33m;
            }

            if (ema50 > ema200)
            {
                bullPoints += 0.33m;
            }

            if (isPriceAboveEma200)
            {
                bullPoints += 0.34m;
            }

            var bearPoints = 0m;
            if (ema20 < ema50)
            {
                bearPoints += 0.33m;
            }

            if (ema50 < ema200)
            {
                bearPoints += 0.33m;
            }

            if (isPriceBelowEma200)
            {
                bearPoints += 0.34m;
            }

            baseScore = Math.Min(Math.Max(bullPoints, bearPoints), 0.49m);
        }

        var isDirectedTrend = trend is MarketTrend.Bullish or MarketTrend.Bearish;
        var volumeBoost = isDirectedTrend && volumeRatio > 1m
            ? Math.Min((volumeRatio - 1m) * 0.1m, MaxVolumeBoost)
            : 0m;

        var strengthScore = Math.Min(baseScore + volumeBoost, 1m);

        return (trend, Math.Round(strengthScore, 4));
    }
}
