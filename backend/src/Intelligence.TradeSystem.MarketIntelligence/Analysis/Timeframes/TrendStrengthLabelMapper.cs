using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

/// <summary>
/// Централизованное отображение <c>trendStrengthLabel</c> из <see cref="MarketTrend"/> и числового score.
///
/// Whitelist допустимых значений V1:
/// <list type="table">
///   <listheader><term>Label</term><description>Условие</description></listheader>
///   <item><term>Undefined</term><description>trend == Unknown (score игнорируется)</description></item>
///   <item><term>Strong</term>   <description>trend != Unknown &amp;&amp; score &gt;= 0.80</description></item>
///   <item><term>Moderate</term> <description>trend != Unknown &amp;&amp; score &gt;= 0.50 &amp;&amp; score &lt; 0.80</description></item>
///   <item><term>Weak</term>     <description>trend != Unknown &amp;&amp; score &lt; 0.50</description></item>
/// </list>
///
/// Инварианты:
/// - trend == Unknown           →  label == Undefined (независимо от score)
/// - trend == Sideways/Bullish/Bearish →  label ∈ {Strong, Moderate, Weak}
/// - label == Undefined         ←→ trend == Unknown
/// </summary>
internal static class TrendStrengthLabelMapper
{
    /// <summary>Минимальный score для метки <see cref="TrendStrengthLabel.Strong"/>.</summary>
    internal const decimal StrongThreshold = 0.80m;

    /// <summary>Минимальный score для метки <see cref="TrendStrengthLabel.Moderate"/>.</summary>
    internal const decimal ModerateThreshold = 0.50m;

    /// <summary>
    /// Отображает <see cref="MarketTrend"/> и числовой score в <see cref="TrendStrengthLabel"/>.
    /// </summary>
    /// <param name="trend">Направление тренда из технического анализа.</param>
    /// <param name="score">Нормализованный score силы тренда [0, 1].</param>
    /// <returns>Детерминированная метка силы тренда.</returns>
    public static TrendStrengthLabel Map(MarketTrend trend, decimal score)
    {
        if (trend == MarketTrend.Unknown) return TrendStrengthLabel.Undefined;
        if (score >= StrongThreshold) return TrendStrengthLabel.Strong;
        if (score >= ModerateThreshold) return TrendStrengthLabel.Moderate;
        return TrendStrengthLabel.Weak;
    }
}
