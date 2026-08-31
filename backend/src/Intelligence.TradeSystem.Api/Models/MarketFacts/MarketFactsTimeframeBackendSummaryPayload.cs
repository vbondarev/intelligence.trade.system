namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Backend-generated summary и флаги для таймфрейма.
/// Содержит детерминированные оценки — не LLM-анализ.
/// </summary>
public sealed record MarketFactsTimeframeBackendSummaryPayload
{
    /// <summary>Bias таймфрейма. Например: <c>bullish</c>, <c>bearish</c>, <c>neutral</c>.</summary>
    public string? Bias { get; init; }

    /// <summary>Признак подтверждённого тренда.</summary>
    public bool? IsTrendConfirmed { get; init; }

    /// <summary>Состояние моментума. Например: <c>accelerating</c>, <c>fading</c>.</summary>
    public string? MomentumState { get; init; }

    /// <summary>Оценка качества входа. Например: <c>high</c>, <c>medium</c>, <c>low</c>.</summary>
    public string? EntryQuality { get; init; }

    /// <summary>Список risk-флагов для данного таймфрейма.</summary>
    public required IReadOnlyList<string> RiskFlags { get; init; }
}
