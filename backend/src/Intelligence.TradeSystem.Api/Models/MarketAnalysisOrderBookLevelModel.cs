namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO одного ценового уровня стакана.</summary>
public sealed record MarketAnalysisOrderBookLevelModel
{
    public decimal Price { get; init; }
    public decimal Size { get; init; }
}
