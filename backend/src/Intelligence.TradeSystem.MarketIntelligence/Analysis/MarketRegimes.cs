namespace Intelligence.TradeSystem.MarketIntelligence.Analysis;

/// <summary>
/// Канонические строковые обозначения рыночных режимов, используемые в аналитических снапшотах.
/// </summary>
public static class MarketRegimes
{
    /// <summary>Направленный рынок с согласованным движением и достаточной силой тренда.</summary>
    public const string Trending = "Trending";

    /// <summary>Рынок с повышенной турбулентностью, конфликтующими сигналами или всплеском объёма.</summary>
    public const string Volatile = "Volatile";

    /// <summary>Рынок, склонный к возврату к среднему после экстремумов или боковой консолидации.</summary>
    public const string MeanReversion = "MeanReversion";

    /// <summary>Нейтральный режим без выраженного доминирующего сценария.</summary>
    public const string Neutral = "Neutral";
}
