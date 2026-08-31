namespace Intelligence.TradeSystem.MarketIntelligence.Snapshots;

/// <summary>
/// Агрегированные оценки настроения рынка, синтезированные из нескольких независимых сигналов.
/// Все скоры нормализованы в диапазоне [−1, 1]:
/// −1 — максимально медвежий, +1 — максимально бычий сигнал.
/// </summary>
public sealed record SentimentSnapshot
{
    /// <summary>
    /// Оценка смещения позиционирования участников на основе соотношения лонг/шорт.
    /// Положительное значение — доминируют лонги, отрицательное — шорты.
    /// </summary>
    public decimal LongShortBiasScore { get; init; }

    /// <summary>
    /// Оценка настроения на основе ставки финансирования.
    /// Высокая положительная ставка (лонги переплачивают) даёт отрицательный скор
    /// как контрарный сигнал перегрева, и наоборот.
    /// </summary>
    public decimal FundingBiasScore { get; init; }

    /// <summary>
    /// Оценка давления стакана заявок на основе дисбаланса бид/аск объёмов.
    /// Положительное значение — доминируют покупатели, отрицательное — продавцы.
    /// </summary>
    public decimal OrderBookPressureScore { get; init; }

    /// <summary>
    /// Оценка давления потока сделок на основе дельты объёма (taker buy vs. taker sell).
    /// Положительное значение — агрессивные покупки, отрицательное — агрессивные продажи.
    /// </summary>
    public decimal TradeFlowPressureScore { get; init; }

    /// <summary>
    /// Классификация текущего рыночного режима.
    /// Возможные значения: <c>Trending</c>, <c>MeanReversion</c>, <c>Volatile</c>, <c>Neutral</c>.
    /// </summary>
    public string MarketRegime { get; init; } = string.Empty;
}
