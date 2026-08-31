namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Значения технических индикаторов таймфрейма.
/// </summary>
public sealed record MarketFactsTimeframeIndicatorsPayload
{
    /// <summary>EMA 20.</summary>
    public decimal? Ema20 { get; init; }

    /// <summary>EMA 50.</summary>
    public decimal? Ema50 { get; init; }

    /// <summary>EMA 200.</summary>
    public decimal? Ema200 { get; init; }

    /// <summary>RSI 14.</summary>
    public decimal? Rsi14 { get; init; }

    /// <summary>Признак надёжности RSI 14 (достаточно баров для расчёта).</summary>
    public bool? Rsi14IsReliable { get; init; }

    /// <summary>ATR 14.</summary>
    public decimal? Atr14 { get; init; }

    /// <summary>Отношение текущего объёма к среднему.</summary>
    public decimal? VolumeRatio { get; init; }
}
