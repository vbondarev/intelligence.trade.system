using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

/// <summary>
/// Строит read model портфеля из восстановленного сохранённого снимка.
/// </summary>
public static class PortfolioReadProjection
{
    /// <summary>
    /// Проецирует снимок на момент <paramref name="asOf"/>: свежесть оценивается на момент чтения,
    /// экспозиция группируется по активу расчёта, общий PnL берётся из account-level значения.
    /// </summary>
    public static PortfolioReadSummary Project(PortfolioState state, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new PortfolioReadSummary(
            state.ExchangeAccountId,
            state.CalculatedAt,
            state.Capital.TotalEquity,
            state.Capital.AvailableCapital,
            state.Capital.TotalWalletBalance,
            state.Capital.ObservedAt,
            state.GrossExposure,
            state.LongExposure,
            state.ShortExposure,
            state.NetExposure,
            state.Capital.AccountUnrealizedPnl,
            state.UsedCapital,
            state.FreeCapital,
            state.FreeCapitalPercent,
            state.GrossExposureToEquityPercent,
            state.LargestPositionConcentrationPercent,
            state.LargestPositionId,
            state.PositionsFullyReconciled,
            state.IsComplete,
            state.IsFreshAt(asOf),
            state.Positions.Count,
            ProjectExposures(state.Positions));
    }

    /// <summary>
    /// Группирует экспозицию позиций снимка по активу расчёта без суммирования между активами.
    /// Группы упорядочены по значению актива ordinal-сравнением.
    /// </summary>
    public static IReadOnlyList<PortfolioExposureReadSummary> ProjectExposures(
        IEnumerable<PortfolioPositionState> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        return positions
            .Where(position => position.TrackingState != PositionTrackingState.Closed)
            .GroupBy(position => position.SettlementAsset)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => new PortfolioExposureReadSummary(
                group.Key,
                SumKnown(group.Select(position => position.PositionValue)),
                SumKnown(group
                    .Where(position => position.PositionSide == PositionSide.Long)
                    .Select(position => position.PositionValue)),
                SumKnown(group
                    .Where(position => position.PositionSide == PositionSide.Short)
                    .Select(position => position.PositionValue))))
            .ToArray();
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => !value.HasValue)
            ? null
            : materialized.Sum(value => value!.Value);
    }
}
