namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO технического анализа одного таймфрейма.</summary>
public sealed record TimeframeModel
{
    public required string Timeframe { get; init; }
    public DateTimeOffset LastCandleOpenTimeUtc { get; init; }
    public required CandleModel LastCandle { get; init; }
    public decimal Ema20 { get; init; }
    public decimal Ema50 { get; init; }
    public decimal Ema200 { get; init; }
    public decimal Rsi14 { get; init; }
    public decimal Atr14 { get; init; }
    public decimal VolumeSma20 { get; init; }
    public decimal VolumeRatio { get; init; }
    public decimal TrendStrengthScore { get; init; }
    public required string Trend { get; init; }
    public decimal Support1 { get; init; }
    public decimal Support2 { get; init; }
    public decimal Resistance1 { get; init; }
    public decimal Resistance2 { get; init; }
    public bool IsAboveEma20 { get; init; }
    public bool IsAboveEma50 { get; init; }
    public bool IsAboveEma200 { get; init; }
    public bool EmaBullishAlignment { get; init; }
    public bool EmaBearishAlignment { get; init; }
    public bool RsiOverbought { get; init; }
    public bool RsiOversold { get; init; }
    public decimal CandleRangePct { get; init; }
    public decimal DistanceToSupport1Pct { get; init; }
    public decimal DistanceToResistance1Pct { get; init; }
}
