namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO агрегированного потока сделок.</summary>
public sealed record TradeFlowModel
{
    public DateTimeOffset WindowStartUtc { get; init; }
    public DateTimeOffset WindowEndUtc { get; init; }
    public decimal BuyVolume { get; init; }
    public decimal SellVolume { get; init; }
    public decimal DeltaVolume { get; init; }
    public decimal DeltaPct { get; init; }
    public int TotalTrades { get; init; }
    public int BuyTrades { get; init; }
    public int SellTrades { get; init; }
    public decimal AvgTradeSize { get; init; }
    public decimal MaxTradeSize { get; init; }
    public bool HasAggressiveBuyPressure { get; init; }
    public bool HasAggressiveSellPressure { get; init; }
}
