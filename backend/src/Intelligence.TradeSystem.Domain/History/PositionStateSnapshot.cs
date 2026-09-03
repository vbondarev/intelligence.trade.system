namespace Intelligence.TradeSystem.Domain.History;

/// <summary>
/// Неизменяемый снимок изменяемых полей <see cref="Position"/> в определённый момент времени.
/// Используется для восстановления состояния позиции из истории изменений
/// (<see cref="PositionChange"/>) без обращения к текущему объекту <see cref="Position"/>.
/// </summary>
public sealed record PositionStateSnapshot(
    decimal Size,
    decimal? AverageEntryPrice,
    decimal? PositionValue,
    decimal? Leverage,
    decimal? MarkPrice,
    decimal? BreakEvenPrice,
    decimal? LiquidationPrice,
    decimal? UnrealizedPnl,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop);
