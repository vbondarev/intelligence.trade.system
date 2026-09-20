using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed record PositionReadQuery(
    ExchangeAccountId? ExchangeAccountId,
    IReadOnlyCollection<PositionTrackingState> TrackingStates,
    string? Symbol,
    PositionSide? Side,
    int PageSize,
    PositionReadCursor? Cursor)
{
    public static PositionReadQuery Create(
        ExchangeAccountId? exchangeAccountId,
        PositionTrackingState? trackingState,
        string? symbol,
        PositionSide? side,
        int pageSize,
        PositionReadCursor? cursor)
    {
        var trackingStates = trackingState is null
            ? new[]
            {
                PositionTrackingState.Active,
                PositionTrackingState.Unknown,
                PositionTrackingState.Stale,
            }
            : [trackingState.Value];

        return new PositionReadQuery(
            exchangeAccountId,
            trackingStates,
            string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim(),
            side,
            pageSize,
            cursor);
    }
}

public readonly record struct PositionReadCursor(
    DateTimeOffset FirstDetectedAt,
    PositionId PositionId);

public sealed record PositionReadPage(
    IReadOnlyList<PositionReadListItem> Items,
    PositionReadCursor? NextCursor,
    bool HasMore);

public sealed record PositionReadListItem(
    PositionId Id,
    ExchangeAccountId ExchangeAccountId,
    string Symbol,
    PositionSide Side,
    PositionTrackingState TrackingState,
    decimal Size,
    decimal? AverageEntryPrice,
    decimal? MarkPrice,
    decimal? PositionValue,
    decimal? UnrealizedPnl,
    decimal? Leverage,
    decimal? LiquidationPrice,
    DateTimeOffset FirstDetectedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ClosedAt);

public sealed record PositionReadDetail(
    PositionReadListItem ListItem,
    MarketCategory MarketCategory,
    decimal? BreakEvenPrice,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop);

public sealed record PortfolioReadSummary(
    ExchangeAccountId ExchangeAccountId,
    DateTimeOffset CalculatedAt,
    decimal? TotalEquity,
    decimal? AvailableCapital,
    decimal? TotalWalletBalance,
    DateTimeOffset? CapitalObservedAt,
    decimal? GrossExposure,
    decimal? LongExposure,
    decimal? ShortExposure,
    decimal? NetExposure,
    decimal? TotalUnrealizedPnl,
    decimal? UsedCapital,
    decimal? FreeCapital,
    decimal? FreeCapitalPercent,
    decimal? GrossExposureToEquityPercent,
    decimal? LargestPositionConcentrationPercent,
    PositionId? LargestPositionId,
    bool PositionsFullyReconciled,
    bool IsComplete,
    bool IsFresh);

public sealed record PortfolioReadResult(
    bool AccountExists,
    PortfolioReadSummary? Summary);
