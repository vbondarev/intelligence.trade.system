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

/// <remarks>
/// <see cref="PositionValue"/> и <see cref="UnrealizedPnl"/> выражены в <see cref="SettlementAsset"/>.
/// </remarks>
public sealed record PositionReadListItem(
    PositionId Id,
    ExchangeAccountId ExchangeAccountId,
    string Symbol,
    SettlementAsset SettlementAsset,
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

/// <summary>
/// Account-scoped read model последнего сохранённого снимка портфеля.
/// </summary>
/// <remarks>
/// <see cref="TotalUnrealizedPnl"/> — нормализованный биржей account-level PnL в USD, а не сумма
/// position-level PnL. <see cref="Exposures"/> сгруппированы по активу расчёта и никогда не
/// суммируются между разными активами. <see cref="IsFresh"/> вычисляется на момент чтения.
/// </remarks>
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
    bool IsFresh,
    int CurrentPositionCount,
    IReadOnlyList<PortfolioExposureReadSummary> Exposures);

/// <summary>
/// Экспозиция позиций снимка портфеля в пределах одного актива расчёта.
/// </summary>
/// <remarks>
/// Агрегат равен <see langword="null"/>, если стоимость хотя бы одной входящей в него позиции
/// неизвестна; при отсутствии позиций соответствующей стороны агрегат равен нулю.
/// </remarks>
public sealed record PortfolioExposureReadSummary(
    SettlementAsset SettlementAsset,
    decimal? GrossExposure,
    decimal? LongExposure,
    decimal? ShortExposure);

public sealed record PortfolioReadResult(
    bool AccountExists,
    PortfolioReadSummary? Summary);
