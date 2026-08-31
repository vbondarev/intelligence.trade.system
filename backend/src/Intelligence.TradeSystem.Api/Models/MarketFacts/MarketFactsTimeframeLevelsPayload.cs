namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Уровни поддержки и сопротивления на таймфрейме.
/// </summary>
public sealed record MarketFactsTimeframeLevelsPayload
{
    /// <summary>Первый уровень поддержки.</summary>
    public decimal? Support1 { get; init; }

    /// <summary>Второй уровень поддержки.</summary>
    public decimal? Support2 { get; init; }

    /// <summary>Первый уровень сопротивления.</summary>
    public decimal? Resistance1 { get; init; }

    /// <summary>Второй уровень сопротивления.</summary>
    public decimal? Resistance2 { get; init; }

    /// <summary>Расстояние до первого уровня поддержки в процентах от текущей цены.</summary>
    public decimal? DistanceToSupport1Pct { get; init; }

    /// <summary>Расстояние до первого уровня сопротивления в процентах от текущей цены.</summary>
    public decimal? DistanceToResistance1Pct { get; init; }

    /// <summary>Метаданные первого уровня поддержки.</summary>
    public MarketFactsLevelMetaPayload? Support1Meta { get; init; }

    /// <summary>Метаданные второго уровня поддержки.</summary>
    public MarketFactsLevelMetaPayload? Support2Meta { get; init; }

    /// <summary>Метаданные первого уровня сопротивления.</summary>
    public MarketFactsLevelMetaPayload? Resistance1Meta { get; init; }

    /// <summary>Метаданные второго уровня сопротивления.</summary>
    public MarketFactsLevelMetaPayload? Resistance2Meta { get; init; }
}
