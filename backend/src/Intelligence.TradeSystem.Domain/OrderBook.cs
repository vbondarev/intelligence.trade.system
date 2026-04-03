namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Сырой снимок стакана заявок торгового инструмента — данные с биржи без агрегации.
/// Содержит полный список уровней бидов и асков в том виде, в котором они получены от API.
/// Агрегированные метрики (дисбаланс, ликвидные стены и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record OrderBook(
    string Symbol,
    MarketCategory Category,
    DateTimeOffset CapturedAt,
    IReadOnlyList<OrderBookEntry> Bids,
    IReadOnlyList<OrderBookEntry> Asks)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: спот, линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Момент времени (UTC), в который был получен снимок стакана.</summary>
    public DateTimeOffset CapturedAt { get; init; } = CapturedAt;

    /// <summary>
    /// Уровни бидов, отсортированные по убыванию цены (лучший бид — первый).
    /// </summary>
    public IReadOnlyList<OrderBookEntry> Bids { get; init; } = Bids;

    /// <summary>
    /// Уровни асков, отсортированные по возрастанию цены (лучший аск — первый).
    /// </summary>
    public IReadOnlyList<OrderBookEntry> Asks { get; init; } = Asks;
}

