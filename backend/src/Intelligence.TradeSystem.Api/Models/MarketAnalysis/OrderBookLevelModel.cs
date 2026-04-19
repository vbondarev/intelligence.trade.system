namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO одного ценового уровня стакана.</summary>
public sealed record OrderBookLevelModel
{
    public decimal Price { get; init; }
    public decimal Size { get; init; }
}
