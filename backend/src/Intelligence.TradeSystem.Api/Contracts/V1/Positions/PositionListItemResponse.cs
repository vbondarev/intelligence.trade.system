namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

/// <summary>Краткое текущее состояние позиции в списке.</summary>
/// <remarks>
/// <see cref="PositionValue"/> и <see cref="UnrealizedPnl"/> выражены в <see cref="SettlementAsset"/>
/// позиции, а не в USD.
/// </remarks>
public sealed record PositionListItemResponse(
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
    string SettlementAsset);
