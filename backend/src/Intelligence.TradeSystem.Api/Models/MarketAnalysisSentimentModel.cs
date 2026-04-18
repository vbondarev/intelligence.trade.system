namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO агрегированного рыночного сентимента.</summary>
public sealed record MarketAnalysisSentimentModel
{
    public decimal LongShortBiasScore { get; init; }
    public decimal FundingBiasScore { get; init; }
    public decimal OrderBookPressureScore { get; init; }
    public decimal TradeFlowPressureScore { get; init; }
    public required string MarketRegime { get; init; }
}
