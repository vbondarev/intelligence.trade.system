namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Агрегированный уровень поддержки или сопротивления с метаданными.
/// </summary>
public sealed record MarketFactsAggregatedLevelPayload
{
    /// <summary>Таймфрейм источника уровня. Например: <c>1h</c>, <c>4h</c>.</summary>
    public required string Timeframe { get; init; }

    /// <summary>Ранг уровня в списке (1 — ближайший/наиболее значимый).</summary>
    public required int Rank { get; init; }

    /// <summary>
    /// Тип уровня. Ожидаемые значения: <c>support</c>, <c>resistance</c>.
    /// </summary>
    public required string Kind { get; init; }

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
