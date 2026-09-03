using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>
/// Неизменяемый бизнес-снимок текущего состояния одного биржевого аккаунта.
/// </summary>
public sealed class PortfolioState
{
    private PortfolioState(
        ExchangeAccountId exchangeAccountId,
        IReadOnlyList<PortfolioPositionState> positions,
        PortfolioCapitalState capital,
        DateTimeOffset calculatedAt,
        TimeSpan staleAfter)
    {
        ExchangeAccountId = exchangeAccountId;
        Positions = positions;
        Capital = capital;
        CalculatedAt = calculatedAt;
        StaleAfter = staleAfter;

        var values = positions.Where(p => p.TrackingState != PositionTrackingState.Closed).ToArray();
        GrossExposure = SumKnown(values.Select(p => p.PositionValue));
        LongExposure = SumKnown(values.Where(p => p.PositionSide == PositionSide.Long).Select(p => p.PositionValue));
        ShortExposure = SumKnown(values.Where(p => p.PositionSide == PositionSide.Short).Select(p => p.PositionValue));
        NetExposure = LongExposure.HasValue && ShortExposure.HasValue
            ? LongExposure.Value - ShortExposure.Value
            : null;
        TotalUnrealizedPnl = SumKnown(values.Select(p => p.UnrealizedPnl));
        UsedCapital = capital.TotalEquity.HasValue && capital.AvailableCapital.HasValue
            ? capital.TotalEquity.Value - capital.AvailableCapital.Value
            : null;
        FreeCapital = capital.AvailableCapital;
        FreeCapitalPercent = capital.TotalEquity > 0m
            ? FreeCapital / capital.TotalEquity * 100m
            : null;
        GrossExposureToEquityPercent = GrossExposure.HasValue && capital.TotalEquity > 0m
            ? GrossExposure / capital.TotalEquity * 100m
            : null;

        if (GrossExposure is null)
        {
            LargestPositionConcentrationPercent = null;
            LargestPositionId = null;
        }
        else if (GrossExposure == 0m)
        {
            LargestPositionConcentrationPercent = 0m;
            LargestPositionId = null;
        }
        else
        {
            var largest = values.Where(p => p.PositionValue.HasValue)
                .OrderByDescending(p => p.PositionValue)
                .ToArray();
            var largestValue = largest[0].PositionValue!.Value;
            LargestPositionConcentrationPercent = largestValue / GrossExposure.Value * 100m;
            LargestPositionId = largest.Length == 1 ||
                largest[1].PositionValue != largestValue
                ? largest[0].PositionId
                : null;
        }

        IsComplete = capital.TotalEquity > 0m &&
            capital.AvailableCapital.HasValue &&
            values.All(p => p.PositionValue.HasValue && p.UnrealizedPnl.HasValue);
        IsFresh = capital.ObservedAt.HasValue &&
            calculatedAt >= capital.ObservedAt.Value &&
            calculatedAt - capital.ObservedAt.Value <= staleAfter &&
            values.All(p =>
                p.TrackingState is not (PositionTrackingState.Unknown or PositionTrackingState.Stale) &&
                calculatedAt >= p.LastObservedAt &&
                calculatedAt - p.LastObservedAt <= staleAfter);
    }

    public ExchangeAccountId ExchangeAccountId { get; }
    public IReadOnlyList<PortfolioPositionState> Positions { get; }
    public PortfolioCapitalState Capital { get; }
    public DateTimeOffset CalculatedAt { get; }
    public TimeSpan StaleAfter { get; }
    public decimal? GrossExposure { get; }
    public decimal? LongExposure { get; }
    public decimal? ShortExposure { get; }
    public decimal? NetExposure { get; }
    public decimal? TotalUnrealizedPnl { get; }
    public decimal? UsedCapital { get; }
    public decimal? FreeCapital { get; }
    public decimal? FreeCapitalPercent { get; }
    public decimal? GrossExposureToEquityPercent { get; }
    public decimal? LargestPositionConcentrationPercent { get; }
    public PositionId? LargestPositionId { get; }
    public bool IsComplete { get; }
    public bool IsFresh { get; }

    public static PortfolioState Create(
        ExchangeAccountId exchangeAccountId,
        IEnumerable<Position> positions,
        PortfolioCapitalState capital,
        DateTimeOffset calculatedAt,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(capital);

        if (exchangeAccountId == default)
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        if (staleAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(staleAfter), staleAfter, "StaleAfter cannot be negative.");
        if (capital.ObservedAt.HasValue && calculatedAt < capital.ObservedAt.Value)
            throw new ArgumentException(
                "CalculatedAt cannot precede the capital observation.", nameof(calculatedAt));

        var sourcePositions = positions.ToArray();
        foreach (var position in sourcePositions)
        {
            ArgumentNullException.ThrowIfNull(position);
            if (position.ExchangePositionKey.ExchangeAccountId != exchangeAccountId)
                throw new ArgumentException(
                    "All positions must belong to the portfolio account.", nameof(positions));
        }

        var snapshots = sourcePositions
            .Where(position => position.TrackingState != PositionTrackingState.Closed)
            .Select(position =>
        {
            if (position.TrackingState != PositionTrackingState.Closed && calculatedAt < position.LastObservedAt)
                throw new ArgumentException(
                    "CalculatedAt cannot precede an included position observation.", nameof(calculatedAt));

            return new PortfolioPositionState(
                position.Id,
                position.ExchangePositionKey,
                position.MarketCategory,
                position.ExchangePositionKey.PositionSide,
                position.TrackingState,
                position.Size,
                position.PositionValue,
                position.UnrealizedPnl,
                position.AverageEntryPrice,
                position.MarkPrice,
                position.LiquidationPrice,
                position.Leverage,
                position.LastObservedAt);
        }).ToArray();

        return new PortfolioState(
            exchangeAccountId,
            new ReadOnlyCollection<PortfolioPositionState>(snapshots),
            capital,
            calculatedAt,
            staleAfter);
    }

    private static decimal? SumKnown(IEnumerable<decimal?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => !value.HasValue)
            ? null
            : materialized.Sum(value => value!.Value);
    }
}
