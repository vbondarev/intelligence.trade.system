namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

/// <summary>Полное текущее состояние позиции без истории и аналитики.</summary>
public sealed record PositionResponse(
    Guid Id,
    Guid ExchangeAccountId,
    string Symbol,
    PositionSideV1 Side,
    PositionTrackingStateV1 TrackingState,
    decimal Size,
    decimal? AverageEntryPrice,
    decimal? MarkPrice,
    decimal? PositionValue,
    decimal? UnrealizedPnl,
    decimal? Leverage,
    decimal? LiquidationPrice,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ClosedAt,
    MarketCategoryV1 MarketCategory,
    decimal? BreakEvenPrice,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop);
