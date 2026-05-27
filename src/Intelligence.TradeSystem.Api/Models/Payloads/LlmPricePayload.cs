namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Текущее состояние цены инструмента.</summary>
public sealed record LlmPricePayload
{
    public required decimal LastPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal IndexPrice { get; init; }
    public required decimal SpreadAbs { get; init; }
    public required decimal SpreadPct { get; init; }
    public required decimal Price24hChangePct { get; init; }
    public required decimal High24h { get; init; }
    public required decimal Low24h { get; init; }
    public required decimal Volume24h { get; init; }
}
