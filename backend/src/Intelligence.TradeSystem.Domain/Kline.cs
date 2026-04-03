namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Одна свеча (candlestick / kline) торгового инструмента.
/// Содержит агрегированные данные о цене и объёме за один временной период.
/// </summary>
public sealed record Kline(
    string Symbol,
    MarketCategory Category,
    KlineInterval Interval,
    DateTime StartTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover
)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: спот, линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Временной интервал свечи (таймфрейм).</summary>
    public KlineInterval Interval { get; init; } = Interval;

    /// <summary>Время открытия свечи (UTC).</summary>
    public DateTime StartTime { get; init; } = StartTime;

    /// <summary>Цена открытия свечи.</summary>
    public decimal Open { get; init; } = Open;

    /// <summary>Максимальная цена за период свечи.</summary>
    public decimal High { get; init; } = High;

    /// <summary>Минимальная цена за период свечи.</summary>
    public decimal Low { get; init; } = Low;

    /// <summary>Цена закрытия свечи.</summary>
    public decimal Close { get; init; } = Close;

    /// <summary>Торговый объём за период (в базовой валюте/контрактах).</summary>
    public decimal Volume { get; init; } = Volume;

    /// <summary>Оборот за период (в котируемой валюте, обычно USDT).</summary>
    public decimal Turnover { get; init; } = Turnover;
}
