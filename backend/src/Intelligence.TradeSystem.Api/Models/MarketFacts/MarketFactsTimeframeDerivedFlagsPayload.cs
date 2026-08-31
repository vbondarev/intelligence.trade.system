namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Производные булевы флаги, вычисленные на основе индикаторов таймфрейма.
/// </summary>
public sealed record MarketFactsTimeframeDerivedFlagsPayload
{
    /// <summary>Цена выше EMA 20.</summary>
    public bool? IsAboveEma20 { get; init; }

    /// <summary>Цена выше EMA 50.</summary>
    public bool? IsAboveEma50 { get; init; }

    /// <summary>Цена выше EMA 200.</summary>
    public bool? IsAboveEma200 { get; init; }

    /// <summary>Бычье выравнивание EMA (EMA20 &gt; EMA50 &gt; EMA200).</summary>
    public bool? EmaBullishAlignment { get; init; }

    /// <summary>Медвежье выравнивание EMA (EMA20 &lt; EMA50 &lt; EMA200).</summary>
    public bool? EmaBearishAlignment { get; init; }

    /// <summary>RSI в зоне перекупленности.</summary>
    public bool? RsiOverbought { get; init; }

    /// <summary>RSI в зоне перепроданности.</summary>
    public bool? RsiOversold { get; init; }
}
