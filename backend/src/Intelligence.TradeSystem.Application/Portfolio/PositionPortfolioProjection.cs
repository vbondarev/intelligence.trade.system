using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Текущее состояние позиции для сборки портфеля без загрузки lifecycle history.
/// </summary>
public sealed record PositionPortfolioProjection(
    PositionId PositionId,
    ConcurrencyVersion Version,
    PortfolioPositionState State)
{
    public static PositionPortfolioProjection From(Versioned<Position> position) =>
        new(position.Value.Id, position.Version, ToSnapshot(position.Value));

    public static PortfolioPositionState ToSnapshot(Position position) =>
        new(
            position.Id,
            position.ExchangePositionKey,
            position.MarketCategory,
            position.SettlementAsset,
            position.ExchangePositionKey.PositionSide,
            position.TrackingState,
            position.Size,
            position.PositionValue,
            position.UnrealizedPnl,
            position.AverageEntryPrice,
            position.MarkPrice,
            position.LiquidationPrice,
            position.Leverage,
            position.LastObservedAt);
}
