using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Stable versioned snapshot of the position state at the time of a lifecycle event.
/// </summary>
public sealed record PositionStateEventPayloadV1(
    decimal Size,
    decimal? AverageEntryPrice,
    decimal? PositionValue,
    decimal? Leverage,
    decimal? MarkPrice,
    decimal? BreakEvenPrice,
    decimal? LiquidationPrice,
    decimal? UnrealizedPnl,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop)
{
    public static PositionStateEventPayloadV1 From(PositionStateSnapshot snapshot) =>
        new(
            snapshot.Size,
            snapshot.AverageEntryPrice,
            snapshot.PositionValue,
            snapshot.Leverage,
            snapshot.MarkPrice,
            snapshot.BreakEvenPrice,
            snapshot.LiquidationPrice,
            snapshot.UnrealizedPnl,
            snapshot.TakeProfit,
            snapshot.StopLoss,
            snapshot.TrailingStop);
}
