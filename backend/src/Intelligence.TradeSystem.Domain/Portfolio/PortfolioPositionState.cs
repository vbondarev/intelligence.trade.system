using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>Неизменяемая копия бизнес-позиции, входящая в снимок портфеля.</summary>
/// <remarks>
/// <see cref="PositionValue"/> и <see cref="UnrealizedPnl"/> выражены в <see cref="SettlementAsset"/>;
/// снимок хранит актив расчёта сам и не зависит от текущего состояния позиции.
/// </remarks>
public sealed record PortfolioPositionState(
    PositionId PositionId,
    ExchangePositionKey ExchangePositionKey,
    MarketCategory MarketCategory,
    SettlementAsset SettlementAsset,
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
