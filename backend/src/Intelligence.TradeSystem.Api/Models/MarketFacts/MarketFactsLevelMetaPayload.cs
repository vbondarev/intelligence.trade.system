namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Метаданные уровня поддержки или сопротивления.
/// </summary>
public sealed record MarketFactsLevelMetaPayload
{
    /// <summary>Цена уровня.</summary>
    public decimal? Price { get; init; }

    /// <summary>Числовая оценка силы уровня.</summary>
    public decimal? Strength { get; init; }

    /// <summary>Label силы уровня. Например: <c>strong</c>, <c>moderate</c>, <c>weak</c>.</summary>
    public string? StrengthLabel { get; init; }

    /// <summary>Источник уровня. Например: <c>swing_high</c>, <c>ema200</c>.</summary>
    public string? Source { get; init; }

    /// <summary>Расстояние от текущей цены в процентах.</summary>
    public decimal? DistancePct { get; init; }

    /// <summary>Объём кластера в зоне уровня.</summary>
    public decimal? ClusterVolume { get; init; }
}
