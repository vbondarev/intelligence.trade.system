namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Одна запись истории ставки финансирования — сырые данные с биржи.
/// Содержит значение ставки на конкретный момент начисления.
/// Агрегированные метрики (средние, флаги перегрева и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record FundingRateEntry(
    string Symbol,
    MarketCategory Category,
    DateTimeOffset Timestamp,
    decimal FundingRate)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Момент времени (UTC) начисления ставки финансирования.</summary>
    public DateTimeOffset Timestamp { get; init; } = Timestamp;

    /// <summary>
    /// Ставка финансирования в виде десятичной дроби.
    /// Положительная — лонги платят шортам; отрицательная — наоборот.
    /// Стандартное значение Bybit: <c>0.0001</c> (0.01%) каждые 8 часов.
    /// </summary>
    public decimal FundingRate { get; init; } = FundingRate;
}

