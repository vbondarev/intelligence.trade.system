namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Последнее известное закрытие физического слота инструмента.
/// </summary>
public sealed record LatestClosedPositionSlot(PositionSlotKey Slot, DateTimeOffset ClosedAt);
