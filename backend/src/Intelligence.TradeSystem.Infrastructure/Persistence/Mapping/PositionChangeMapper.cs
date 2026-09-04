using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PositionChangeMapper
{
    public static PositionChangeEntity ToEntity(PositionChange change, int sequence) =>
        new()
        {
            PositionId = change.PositionId.Value,
            Sequence = sequence,
            Kind = change.Kind,
            Cause = change.Cause,
            OccurredAt = PersistenceDateTime.ToUtc(change.OccurredAt),
            TrackingStateAfter = change.TrackingStateAfter,
            BeforeSize = change.Before?.Size,
            BeforeAverageEntryPrice = change.Before?.AverageEntryPrice,
            BeforePositionValue = change.Before?.PositionValue,
            BeforeLeverage = change.Before?.Leverage,
            BeforeMarkPrice = change.Before?.MarkPrice,
            BeforeBreakEvenPrice = change.Before?.BreakEvenPrice,
            BeforeLiquidationPrice = change.Before?.LiquidationPrice,
            BeforeUnrealizedPnl = change.Before?.UnrealizedPnl,
            BeforeTakeProfit = change.Before?.TakeProfit,
            BeforeStopLoss = change.Before?.StopLoss,
            BeforeTrailingStop = change.Before?.TrailingStop,
            AfterSize = change.After.Size,
            AfterAverageEntryPrice = change.After.AverageEntryPrice,
            AfterPositionValue = change.After.PositionValue,
            AfterLeverage = change.After.Leverage,
            AfterMarkPrice = change.After.MarkPrice,
            AfterBreakEvenPrice = change.After.BreakEvenPrice,
            AfterLiquidationPrice = change.After.LiquidationPrice,
            AfterUnrealizedPnl = change.After.UnrealizedPnl,
            AfterTakeProfit = change.After.TakeProfit,
            AfterStopLoss = change.After.StopLoss,
            AfterTrailingStop = change.After.TrailingStop,
        };

    public static PositionChange ToDomain(PositionChangeEntity entity) =>
        new(
            PositionId.FromGuid(entity.PositionId),
            entity.Kind,
            entity.Cause,
            entity.OccurredAt,
            entity.TrackingStateAfter,
            ToSnapshot(
                entity.BeforeSize,
                entity.BeforeAverageEntryPrice,
                entity.BeforePositionValue,
                entity.BeforeLeverage,
                entity.BeforeMarkPrice,
                entity.BeforeBreakEvenPrice,
                entity.BeforeLiquidationPrice,
                entity.BeforeUnrealizedPnl,
                entity.BeforeTakeProfit,
                entity.BeforeStopLoss,
                entity.BeforeTrailingStop),
            new PositionStateSnapshot(
                entity.AfterSize,
                entity.AfterAverageEntryPrice,
                entity.AfterPositionValue,
                entity.AfterLeverage,
                entity.AfterMarkPrice,
                entity.AfterBreakEvenPrice,
                entity.AfterLiquidationPrice,
                entity.AfterUnrealizedPnl,
                entity.AfterTakeProfit,
                entity.AfterStopLoss,
                entity.AfterTrailingStop));

    public static bool IsEquivalent(PositionChangeEntity entity, PositionChange change, int sequence)
    {
        var expected = ToEntity(change, sequence);
        return entity.PositionId == expected.PositionId &&
               entity.Sequence == expected.Sequence &&
               entity.Kind == expected.Kind &&
               entity.Cause == expected.Cause &&
               entity.OccurredAt == expected.OccurredAt &&
               entity.TrackingStateAfter == expected.TrackingStateAfter &&
               entity.BeforeSize == expected.BeforeSize &&
               entity.BeforeAverageEntryPrice == expected.BeforeAverageEntryPrice &&
               entity.BeforePositionValue == expected.BeforePositionValue &&
               entity.BeforeLeverage == expected.BeforeLeverage &&
               entity.BeforeMarkPrice == expected.BeforeMarkPrice &&
               entity.BeforeBreakEvenPrice == expected.BeforeBreakEvenPrice &&
               entity.BeforeLiquidationPrice == expected.BeforeLiquidationPrice &&
               entity.BeforeUnrealizedPnl == expected.BeforeUnrealizedPnl &&
               entity.BeforeTakeProfit == expected.BeforeTakeProfit &&
               entity.BeforeStopLoss == expected.BeforeStopLoss &&
               entity.BeforeTrailingStop == expected.BeforeTrailingStop &&
               entity.AfterSize == expected.AfterSize &&
               entity.AfterAverageEntryPrice == expected.AfterAverageEntryPrice &&
               entity.AfterPositionValue == expected.AfterPositionValue &&
               entity.AfterLeverage == expected.AfterLeverage &&
               entity.AfterMarkPrice == expected.AfterMarkPrice &&
               entity.AfterBreakEvenPrice == expected.AfterBreakEvenPrice &&
               entity.AfterLiquidationPrice == expected.AfterLiquidationPrice &&
               entity.AfterUnrealizedPnl == expected.AfterUnrealizedPnl &&
               entity.AfterTakeProfit == expected.AfterTakeProfit &&
               entity.AfterStopLoss == expected.AfterStopLoss &&
               entity.AfterTrailingStop == expected.AfterTrailingStop;
    }

    private static PositionStateSnapshot? ToSnapshot(
        decimal? size,
        decimal? averageEntryPrice,
        decimal? positionValue,
        decimal? leverage,
        decimal? markPrice,
        decimal? breakEvenPrice,
        decimal? liquidationPrice,
        decimal? unrealizedPnl,
        decimal? takeProfit,
        decimal? stopLoss,
        decimal? trailingStop)
    {
        if (!size.HasValue)
            return null;

        return new PositionStateSnapshot(
            size.Value,
            averageEntryPrice,
            positionValue,
            leverage,
            markPrice,
            breakEvenPrice,
            liquidationPrice,
            unrealizedPnl,
            takeProfit,
            stopLoss,
            trailingStop);
    }
}
