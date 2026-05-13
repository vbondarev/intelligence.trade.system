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
    public decimal? Rsi14 { get; init; }
    /// <summary><c>true</c> — RSI рассчитан на основе достаточного количества свечей.</summary>
    public bool Rsi14IsReliable { get; init; }
    public decimal Atr14 { get; init; }
    public decimal VolumeSma20 { get; init; }
    public decimal VolumeRatio { get; init; }
    public decimal TrendStrengthScore { get; init; }
    public required string Trend { get; init; }
    /// <summary>Ближайший уровень поддержки. <c>null</c> — не обнаружен.</summary>
    public decimal? Support1 { get; init; }
    /// <summary>Второй уровень поддержки. <c>null</c> — не обнаружен.</summary>
    public decimal? Support2 { get; init; }
    /// <summary>Ближайший уровень сопротивления. <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance1 { get; init; }
    /// <summary>Второй уровень сопротивления. <c>null</c> — не обнаружен.</summary>
    public decimal? Resistance2 { get; init; }
    public bool IsAboveEma20 { get; init; }
    public bool IsAboveEma50 { get; init; }
    public bool IsAboveEma200 { get; init; }
    public bool EmaBullishAlignment { get; init; }
    public bool EmaBearishAlignment { get; init; }
    public bool RsiOverbought { get; init; }
    public bool RsiOversold { get; init; }
    public decimal CandleRangePct { get; init; }
    /// <summary>Расстояние до <c>support1</c> в процентах. <c>null</c> — если уровень не обнаружен.</summary>
    public decimal? DistanceToSupport1Pct { get; init; }
    /// <summary>Расстояние до <c>resistance1</c> в процентах. <c>null</c> — если уровень не обнаружен.</summary>
    public decimal? DistanceToResistance1Pct { get; init; }
}
