namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO одной открытой позиции.</summary>
public sealed record MarketAnalysisOpenPositionModel
{
    public required string Symbol { get; init; }
    public required string Side { get; init; }
    public decimal Size { get; init; }
    public decimal AvgPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal BreakEvenPrice { get; init; }
    public decimal LiquidationPrice { get; init; }
    public decimal PositionValueUsd { get; init; }
    public decimal Leverage { get; init; }
    public decimal UnrealizedPnlUsd { get; init; }
    public decimal UnrealizedPnlPct { get; init; }
}
