namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Одна точка исторического ряда соотношения лонг/шорт позиций — сырые данные с биржи.
/// Отражает долю участников рынка, имеющих чистую длинную или короткую позицию
/// в конкретный момент времени.
/// Агрегированные метрики (средние, флаги доминирования) вычисляются в ассемблере.
/// </summary>
public sealed record LongShortRatioEntry(
    string Symbol,
    MarketCategory Category,
    DateTimeOffset Timestamp,
    decimal BuyRatio,
    decimal SellRatio)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Момент времени (UTC) агрегации данных.</summary>
    public DateTimeOffset Timestamp { get; init; } = Timestamp;

    /// <summary>
    /// Доля участников с чистой длинной позицией в диапазоне [0, 1].
    /// Значение выше 0.5 означает преобладание лонгов.
    /// </summary>
    public decimal BuyRatio { get; init; } = BuyRatio;

    /// <summary>
    /// Доля участников с чистой короткой позицией в диапазоне [0, 1].
    /// Значение выше 0.5 означает преобладание шортов.
    /// </summary>
    public decimal SellRatio { get; init; } = SellRatio;
}
