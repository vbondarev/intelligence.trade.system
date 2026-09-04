using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PortfolioStateMapper
{
    public static PortfolioStateEntity ToEntity(PortfolioState state)
    {
        var entity = new PortfolioStateEntity
        {
            ExchangeAccountId = state.ExchangeAccountId.Value,
            TotalEquity = state.Capital.TotalEquity,
            AvailableCapital = state.Capital.AvailableCapital,
            CapitalObservedAt = PersistenceDateTime.ToUtc(state.Capital.ObservedAt),
            TotalWalletBalance = state.Capital.TotalWalletBalance,
            CalculatedAt = PersistenceDateTime.ToUtc(state.CalculatedAt),
            StaleAfter = state.StaleAfter,
            GrossExposure = state.GrossExposure,
            LongExposure = state.LongExposure,
            ShortExposure = state.ShortExposure,
            NetExposure = state.NetExposure,
            TotalUnrealizedPnl = state.TotalUnrealizedPnl,
            UsedCapital = state.UsedCapital,
            FreeCapital = state.FreeCapital,
            FreeCapitalPercent = state.FreeCapitalPercent,
            GrossExposureToEquityPercent = state.GrossExposureToEquityPercent,
            LargestPositionConcentrationPercent = state.LargestPositionConcentrationPercent,
            LargestPositionId = state.LargestPositionId?.Value,
            IsComplete = state.IsComplete,
            IsFresh = state.IsFresh,
        };

        for (var index = 0; index < state.Positions.Count; index++)
        {
            var position = state.Positions[index];
            entity.Positions.Add(new PortfolioPositionStateEntity
            {
                Sequence = index + 1,
                PositionId = position.PositionId.Value,
                ExchangeAccountId = position.ExchangePositionKey.ExchangeAccountId.Value,
                InstrumentId = position.ExchangePositionKey.InstrumentId.Value,
                PositionSide = position.PositionSide,
                PositionIdx = position.ExchangePositionKey.PositionIdx,
                MarketCategory = position.MarketCategory,
                TrackingState = position.TrackingState,
                Size = position.Size,
                PositionValue = position.PositionValue,
                UnrealizedPnl = position.UnrealizedPnl,
                AverageEntryPrice = position.AverageEntryPrice,
                MarkPrice = position.MarkPrice,
                LiquidationPrice = position.LiquidationPrice,
                Leverage = position.Leverage,
                LastObservedAt = PersistenceDateTime.ToUtc(position.LastObservedAt),
            });
        }

        return entity;
    }

    public static PortfolioState ToDomain(
        PortfolioStateEntity entity,
        IReadOnlyCollection<PortfolioPositionStateEntity> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var exchangeAccountId = ExchangeAccountId.FromGuid(entity.ExchangeAccountId);
        var snapshots = positions
            .OrderBy(position => position.Sequence)
            .Select(position =>
            {
                var key = ExchangePositionKey.Create(
                    ExchangeAccountId.FromGuid(position.ExchangeAccountId),
                    InstrumentId.From(position.InstrumentId),
                    position.PositionSide,
                    position.PositionIdx);
                return new PortfolioPositionState(
                    PositionId.FromGuid(position.PositionId),
                    key,
                    position.MarketCategory,
                    position.PositionSide,
                    position.TrackingState,
                    position.Size,
                    position.PositionValue,
                    position.UnrealizedPnl,
                    position.AverageEntryPrice,
                    position.MarkPrice,
                    position.LiquidationPrice,
                    position.Leverage,
                    PersistenceDateTime.ToUtc(position.LastObservedAt));
            })
            .ToArray();

        return PortfolioState.Restore(
            exchangeAccountId,
            snapshots,
            new PortfolioCapitalState(
                entity.TotalEquity,
                entity.AvailableCapital,
                PersistenceDateTime.ToUtc(entity.CapitalObservedAt),
                entity.TotalWalletBalance),
            PersistenceDateTime.ToUtc(entity.CalculatedAt),
            entity.StaleAfter);
    }
}
