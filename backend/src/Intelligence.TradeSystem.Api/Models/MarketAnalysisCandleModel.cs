namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO последней свечи таймфрейма.</summary>
public sealed record MarketAnalysisCandleModel
{
    public DateTimeOffset OpenTimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Turnover { get; init; }
}
