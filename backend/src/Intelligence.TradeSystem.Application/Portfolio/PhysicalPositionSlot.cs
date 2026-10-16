using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Физический торговый слот: аккаунт, категория рынка, символ и <c>PositionIdx</c> без стороны.
/// </summary>
/// <remarks>
/// <see cref="ExchangePositionKey"/> включает <see cref="PositionSide"/> и различает lifecycle,
/// но закрытие или активная позиция одной стороны опровергает устаревшее открытие противоположной
/// стороны того же слота. Hedge mode с разными <c>PositionIdx</c> остаётся независимым.
/// </remarks>
internal static class PhysicalPositionSlot
{
    public static bool Matches(
        Position position,
        ExchangeAccountId exchangeAccountId,
        MarketCategory category,
        string symbol,
        int positionIdx) =>
        position.ExchangePositionKey.ExchangeAccountId == exchangeAccountId &&
        position.MarketCategory == category &&
        position.ExchangePositionKey.PositionIdx == positionIdx &&
        string.Equals(
            position.ExchangePositionKey.InstrumentId.Value,
            symbol.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public static bool Matches(Position position, ExchangePositionKey key, MarketCategory category) =>
        Matches(
            position,
            key.ExchangeAccountId,
            category,
            key.InstrumentId.Value,
            key.PositionIdx);

    public static bool SameSlot(
        ExchangePositionKey left,
        MarketCategory leftCategory,
        ExchangePositionKey right,
        MarketCategory rightCategory) =>
        leftCategory == rightCategory &&
        left.ExchangeAccountId == right.ExchangeAccountId &&
        left.PositionIdx == right.PositionIdx &&
        string.Equals(
            left.InstrumentId.Value,
            right.InstrumentId.Value,
            StringComparison.OrdinalIgnoreCase);
}
