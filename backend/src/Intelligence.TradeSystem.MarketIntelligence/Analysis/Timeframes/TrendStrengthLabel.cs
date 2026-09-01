namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

/// <summary>
/// Метка силы тренда, вычисленная из <c>trendStrengthScore</c>.
/// </summary>
public enum TrendStrengthLabel
{
    /// <summary>Сильный тренд: score &gt;= 0.80.</summary>
    Strong = 1,

    /// <summary>Умеренный тренд: score &gt;= 0.50.</summary>
    Moderate = 2,

    /// <summary>Слабый тренд: score &lt; 0.50.</summary>
    Weak = 3,

    /// <summary>Тренд неопределён: <c>MarketTrend.Unknown</c>.</summary>
    Undefined = 4,
}
