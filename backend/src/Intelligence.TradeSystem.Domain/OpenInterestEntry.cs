namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Одна точка исторического ряда открытого интереса — сырые данные с биржи.
/// Содержит суммарный объём незакрытых контрактов на конкретный момент времени.
/// Агрегированные метрики (изменение, тренд и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record OpenInterestEntry(
    string Symbol,
    MarketCategory Category,
    DateTimeOffset Timestamp,
    decimal OpenInterest)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Момент времени (UTC), к которому относится значение открытого интереса.</summary>
    public DateTimeOffset Timestamp { get; init; } = Timestamp;

    /// <summary>Суммарный объём незакрытых контрактов (в базовых единицах/контрактах).</summary>
    public decimal OpenInterest { get; init; } = OpenInterest;
}

