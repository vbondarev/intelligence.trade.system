namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO значимого уровня концентрации ликвидности.</summary>
public sealed record MarketAnalysisLiquidityWallModel
{
    public decimal Price { get; init; }
    public decimal Size { get; init; }
    public decimal DistancePctFromMarket { get; init; }
}
