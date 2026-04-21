namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO текущего состояния цены инструмента.</summary>
public sealed record PriceModel
{
    public decimal LastPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal IndexPrice { get; init; }
    public decimal BidPrice { get; init; }
    public decimal AskPrice { get; init; }
    public decimal BidSize { get; init; }
    public decimal AskSize { get; init; }
    public decimal SpreadAbs { get; init; }
    public decimal SpreadPct { get; init; }
    public decimal Price24hChangePct { get; init; }
    public decimal High24h { get; init; }
    public decimal Low24h { get; init; }
    public decimal Volume24h { get; init; }
    public decimal Turnover24h { get; init; }
}
