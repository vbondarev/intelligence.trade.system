namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок одной открытой позиции: параметры входа, текущая оценка и уровни риска.
/// </summary>
public sealed record OpenPositionSnapshot
{
    /// <summary>Тикер инструмента, по которому открыта позиция. Например: <c>BTCUSDT</c>.</summary>
    public required string Symbol { get; init; }

    /// <summary>Направление позиции: лонг или шорт.</summary>
    public required PositionSide Side { get; init; }

    /// <summary>Размер позиции (в базовых единицах/контрактах).</summary>
    public decimal Size { get; init; }

    /// <summary>Средняя цена входа в позицию.</summary>
    public decimal AvgPrice { get; init; }

    /// <summary>Текущая расчётная (mark) цена инструмента.</summary>
    public decimal MarkPrice { get; init; }

    /// <summary>
    /// Цена безубыточности с учётом торговых комиссий.
    /// При достижении этой цены позиция закрывается без прибыли и без убытка.
    /// </summary>
    public decimal BreakEvenPrice { get; init; }

    /// <summary>
    /// Цена принудительной ликвидации позиции биржей.
    /// Критический уровень риска — при приближении цены требуется управление позицией.
    /// </summary>
    public decimal LiquidationPrice { get; init; }

    /// <summary>
    /// Стоимость позиции в USD, полученная из исходного <c>OpenPosition.PositionValue</c>.
    /// Маппится как есть; если исходное значение отсутствует, используется <c>0</c>.
    /// </summary>
    public decimal PositionValueUsd { get; init; }

    /// <summary>Кредитное плечо, используемое по позиции.</summary>
    public decimal Leverage { get; init; }

    /// <summary>Нереализованный PnL по позиции в USD.</summary>
    public decimal UnrealizedPnlUsd { get; init; }

    /// <summary>Нереализованный PnL в процентах от стоимости позиции.</summary>
    public decimal UnrealizedPnlPct { get; init; }
}
