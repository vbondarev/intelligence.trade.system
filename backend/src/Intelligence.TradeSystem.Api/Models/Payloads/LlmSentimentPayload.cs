namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Агрегированные оценки сентимента рынка.</summary>
public sealed record LlmSentimentPayload
{
    public required decimal LongShortBiasScore { get; init; }
    public required decimal FundingBiasScore { get; init; }
    public required decimal OrderBookPressureScore { get; init; }
    public required decimal TradeFlowPressureScore { get; init; }
    public required string MarketRegime { get; init; }
}