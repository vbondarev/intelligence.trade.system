namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Агрегированные оценки внутреннего сентимента рынка.
/// Название <c>MarketInternalSentiment</c> используется намеренно,
/// чтобы не путать с будущими Twitter/Telegram sentiment agents.
/// </summary>
public sealed record MarketFactsInternalSentimentPayload
{
    /// <summary>
    /// Скоринг перевеса длинных/коротких позиций.
    /// Положительное значение — преобладают лонги, отрицательное — шорты.
    /// </summary>
    public decimal? LongShortBiasScore { get; init; }

    /// <summary>
    /// Скоринг на основе ставки финансирования.
    /// Положительное значение — рынок перегрет в сторону лонга.
    /// </summary>
    public decimal? FundingBiasScore { get; init; }

    /// <summary>Скоринг давления стакана заявок.</summary>
    public decimal? OrderBookPressureScore { get; init; }

    /// <summary>Скоринг давления потока сделок.</summary>
    public decimal? TradeFlowPressureScore { get; init; }

    /// <summary>
    /// Классификация рыночного режима.
    /// Например: <c>trending_bullish</c>, <c>trending_bearish</c>, <c>ranging</c>, <c>volatile</c>.
    /// </summary>
    public string? MarketRegime { get; init; }
}
