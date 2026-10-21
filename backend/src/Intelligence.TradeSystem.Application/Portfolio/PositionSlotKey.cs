using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Физический слот инструмента; сторона позиции не разделяет slot.
/// </summary>
public readonly record struct PositionSlotKey(InstrumentId InstrumentId, int PositionIdx)
{
    public static PositionSlotKey From(ExchangePositionKey key) =>
        new(key.InstrumentId, key.PositionIdx);
}
