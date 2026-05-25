namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Поток сделок за скользящее временное окно.</summary>
public sealed record LlmTradeFlowPayload
{
    public required DateTimeOffset WindowStartUtc { get; init; }
    public required DateTimeOffset WindowEndUtc { get; init; }
    public required decimal BuyVolume { get; init; }
    public required decimal SellVolume { get; init; }
    public required decimal DeltaVolume { get; init; }
    public required decimal DeltaPct { get; init; }
    public required int BuyTrades { get; init; }
    public required int SellTrades { get; init; }
    public required decimal AvgTradeSize { get; init; }
    public required decimal MaxTradeSize { get; init; }
    public required bool HasAggressiveBuyPressure { get; init; }
    public required bool HasAggressiveSellPressure { get; init; }
}
