namespace Intelligence.TradeSystem.Api.Configuration;

/// <summary>
/// Пороги свежести отдельных секций снапшота для одного режима анализа.
/// </summary>
public sealed record SectionFreshnessOptions
{
    public required TimeSpan PriceMaxAge { get; init; }
    public required TimeSpan DerivativesMaxAge { get; init; }
    public required TimeSpan OrderBookMaxAge { get; init; }
    public required TimeSpan TradeFlowMaxAge { get; init; }
    public required TimeSpan M15MaxAge { get; init; }
    public required TimeSpan H1MaxAge { get; init; }
    public required TimeSpan H4MaxAge { get; init; }
    public required TimeSpan D1MaxAge { get; init; }
    public TimeSpan? PortfolioMaxAge { get; init; }
    public TimeSpan? AggregatedContextMaxAge { get; init; }
}
