using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PositionMapper
{
    public static PositionEntity ToEntity(Position position)
    {
        var entity = new PositionEntity();
        ApplyToEntity(entity, position);
        return entity;
    }

    public static void ApplyToEntity(PositionEntity entity, Position position)
    {
        entity.Id = position.Id.Value;
        entity.ExchangeAccountId = position.ExchangePositionKey.ExchangeAccountId.Value;
        entity.InstrumentId = position.ExchangePositionKey.InstrumentId.Value;
        entity.PositionSide = position.ExchangePositionKey.PositionSide;
        entity.PositionIdx = position.ExchangePositionKey.PositionIdx;
        entity.MarketCategory = position.MarketCategory;
        entity.Size = position.Size;
        entity.AverageEntryPrice = position.AverageEntryPrice;
        entity.PositionValue = position.PositionValue;
        entity.Leverage = position.Leverage;
        entity.MarkPrice = position.MarkPrice;
        entity.BreakEvenPrice = position.BreakEvenPrice;
        entity.LiquidationPrice = position.LiquidationPrice;
        entity.UnrealizedPnl = position.UnrealizedPnl;
        entity.TakeProfit = position.TakeProfit;
        entity.StopLoss = position.StopLoss;
        entity.TrailingStop = position.TrailingStop;
        entity.FirstDetectedAt = PersistenceDateTime.ToUtc(position.FirstDetectedAt);
        entity.LastObservedAt = PersistenceDateTime.ToUtc(position.LastObservedAt);
        entity.ClosedAt = PersistenceDateTime.ToUtc(position.ClosedAt);
        entity.TrackingState = position.TrackingState;
    }

    public static Position ToDomain(
        PositionEntity entity,
        IReadOnlyCollection<PositionChangeEntity> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var positionId = PositionId.FromGuid(entity.Id);
        var key = ExchangePositionKey.Create(
            ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
            InstrumentId.From(entity.InstrumentId),
            entity.PositionSide,
            entity.PositionIdx);
        var domainChanges = changes
            .OrderBy(change => change.Sequence)
            .Select(PositionChangeMapper.ToDomain)
            .ToArray();

        return Position.Restore(
            positionId,
            key,
            entity.MarketCategory,
            entity.Size,
            PersistenceDateTime.ToUtc(entity.FirstDetectedAt),
            PersistenceDateTime.ToUtc(entity.LastObservedAt),
            entity.TrackingState,
            PersistenceDateTime.ToUtc(entity.ClosedAt),
            domainChanges,
            entity.AverageEntryPrice,
            entity.PositionValue,
            entity.Leverage,
            entity.MarkPrice,
            entity.BreakEvenPrice,
            entity.LiquidationPrice,
            entity.UnrealizedPnl,
            entity.TakeProfit,
            entity.StopLoss,
            entity.TrailingStop);
    }
}
