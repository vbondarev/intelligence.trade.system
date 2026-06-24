namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Открытая позиция в портфеле.</summary>
public sealed record LlmOpenPositionPayload
{
    public required string Symbol { get; init; }

    /// <summary>Сторона позиции: <c>Long</c> или <c>Short</c>.</summary>
    public required string Side { get; init; }

    public required decimal Size { get; init; }
    public required decimal AvgPrice { get; init; }
    public required decimal UnrealizedPnlPct { get; init; }
    public required decimal Leverage { get; init; }
    public required decimal LiquidationPrice { get; init; }
}
