using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Открытая позиция на Bybit — сырые данные с биржи.
/// Содержит параметры входа, текущую оценку и уровни риска.
/// Производные поля (процент PnL и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record OpenPosition(
    string Symbol,
    MarketCategory Category,
    PositionSide Side,
    PositionStatus Status,
    decimal Size,
    decimal? AvgPrice,
    decimal? PositionValue,
    decimal? Leverage,
    decimal? MarkPrice,
    decimal? BreakEvenPrice,
    decimal? LiquidationPrice,
    decimal? UnrealizedPnl,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop,
    int RiskId,
    decimal? RiskLimitValue,
    DateTimeOffset? CreatedTime,
    DateTimeOffset? UpdatedTime,
    int PositionIdx = 0)
{
    /// <summary>Тикер инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: линейный или инверсный.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Направление позиции: лонг или шорт.</summary>
    public PositionSide Side { get; init; } = Side;

    /// <summary>Текущий статус позиции.</summary>
    public PositionStatus Status { get; init; } = Status;

    /// <summary>
    /// Размер позиции (в базовых единицах/контрактах).
    /// Всегда больше нуля — позиции с нулевым размером фильтруются на уровне маппера.
    /// </summary>
    public decimal Size { get; init; } = Size;

    /// <summary>Средняя цена входа в позицию.</summary>
    public decimal? AvgPrice { get; init; } = AvgPrice;

    /// <summary>Текущая стоимость позиции в USD: <c>Size × AveragePrice</c>.</summary>
    public decimal? PositionValue { get; init; } = PositionValue;

    /// <summary>Кредитное плечо, используемое по позиции.</summary>
    public decimal? Leverage { get; init; } = Leverage;

    /// <summary>Текущая расчётная (mark) цена инструмента.</summary>
    public decimal? MarkPrice { get; init; } = MarkPrice;

    /// <summary>
    /// Цена безубыточности с учётом торговых комиссий.
    /// При достижении этой цены позиция закрывается без прибыли и без убытка.
    /// </summary>
    public decimal? BreakEvenPrice { get; init; } = BreakEvenPrice;

    /// <summary>
    /// Цена принудительной ликвидации позиции биржей.
    /// <c>null</c> если позиция не рискует ликвидацией (например, кросс-маржа).
    /// </summary>
    public decimal? LiquidationPrice { get; init; } = LiquidationPrice;

    /// <summary>
    /// Нереализованный PnL по позиции в USD.
    /// Положительное — позиция в прибыли, отрицательное — в убытке.
    /// </summary>
    public decimal? UnrealizedPnl { get; init; } = UnrealizedPnl;

    /// <summary>Уровень тейк-профита. <c>null</c> если не установлен.</summary>
    public decimal? TakeProfit { get; init; } = TakeProfit;

    /// <summary>Уровень стоп-лосса. <c>null</c> если не установлен.</summary>
    public decimal? StopLoss { get; init; } = StopLoss;

    /// <summary>Трейлинг-стоп. <c>null</c> если не установлен.</summary>
    public decimal? TrailingStop { get; init; } = TrailingStop;

    /// <summary>Идентификатор уровня риска (риск-лимита).</summary>
    public int RiskId { get; init; } = RiskId;

    /// <summary>Максимальная позиция для текущего риск-лимита (в USD).</summary>
    public decimal? RiskLimitValue { get; init; } = RiskLimitValue;

    /// <summary>Время создания позиции (UTC).</summary>
    public DateTimeOffset? CreatedTime { get; init; } = CreatedTime;

    /// <summary>Время последнего обновления позиции (UTC).</summary>
    public DateTimeOffset? UpdatedTime { get; init; } = UpdatedTime;

    /// <summary>Индекс позиции на бирже для one-way или hedge mode.</summary>
    public int PositionIdx { get; init; } = PositionIdx;
}
