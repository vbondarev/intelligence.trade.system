using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>Неизменяемая копия бизнес-позиции, входящая в снимок портфеля.</summary>
public sealed record PortfolioPositionState(
    PositionId PositionId,
    ExchangePositionKey ExchangePositionKey,
    MarketCategory MarketCategory,
    PositionSide PositionSide,
    PositionTrackingState TrackingState,
    decimal Size,
    decimal? PositionValue,
    decimal? UnrealizedPnl,
    decimal? AverageEntryPrice,
    decimal? MarkPrice,
    decimal? LiquidationPrice,
    decimal? Leverage,
    DateTimeOffset LastObservedAt);
