namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Информация о тренде на таймфрейме.
/// </summary>
public sealed record MarketFactsTimeframeTrendPayload
{
    /// <summary>Текстовое описание тренда. Например: <c>Uptrend</c>, <c>Downtrend</c>.</summary>
    public string? Trend { get; init; }

    /// <summary>Код тренда для программной обработки. Например: <c>uptrend</c>, <c>downtrend</c>.</summary>
    public string? TrendCode { get; init; }

    /// <summary>Числовая оценка силы тренда.</summary>
    public decimal? TrendStrengthScore { get; init; }

    /// <summary>Label силы тренда. Например: <c>strong</c>, <c>moderate</c>, <c>weak</c>.</summary>
    public string? TrendStrengthLabel { get; init; }
}
