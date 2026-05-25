namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Уровень ликвидности (стена) в стакане.</summary>
public sealed record LlmLiquidityWallPayload
{
    public required decimal Price { get; init; }
    public required decimal Size { get; init; }
    public required decimal DistancePctFromMarket { get; init; }
}
