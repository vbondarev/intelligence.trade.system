namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Одна совершённая сделка торгового инструмента — сырые данные с биржи.
/// Содержит цену, объём, сторону-агрессора и временну́ю метку.
/// Агрегированные метрики (дельта объёма, давление и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record Trade(
    string Symbol,
    MarketCategory Category,
    DateTimeOffset Timestamp,
    TradeSide Side,
    decimal Quantity,
    decimal Price)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: спот, линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Момент времени (UTC), в который была совершена сделка.</summary>
    public DateTimeOffset Timestamp { get; init; } = Timestamp;

    /// <summary>
    /// Сторона агрессора сделки: <see cref="TradeSide.Buy"/> — тейкер купил,
    /// <see cref="TradeSide.Sell"/> — тейкер продал.
    /// </summary>
    public TradeSide Side { get; init; } = Side;

    /// <summary>Объём сделки (в базовой валюте / контрактах).</summary>
    public decimal Quantity { get; init; } = Quantity;

    /// <summary>Цена исполнения сделки.</summary>
    public decimal Price { get; init; } = Price;
}
