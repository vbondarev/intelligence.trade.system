namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO значимого уровня концентрации ликвидности.</summary>
public sealed record LiquidityWallModel
{
    public decimal Price { get; init; }
    public decimal Size { get; init; }
    public decimal DistancePctFromMarket { get; init; }
}
