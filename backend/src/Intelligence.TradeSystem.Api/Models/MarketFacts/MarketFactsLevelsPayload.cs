namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Агрегированные уровни поддержки и сопротивления по всем таймфреймам.
/// </summary>
public sealed record MarketFactsLevelsPayload
{
    /// <summary>Агрегированные уровни поддержки, отсортированные по рангу.</summary>
    public required IReadOnlyList<MarketFactsAggregatedLevelPayload> Supports { get; init; }

    /// <summary>Агрегированные уровни сопротивления, отсортированные по рангу.</summary>
    public required IReadOnlyList<MarketFactsAggregatedLevelPayload> Resistances { get; init; }
}
