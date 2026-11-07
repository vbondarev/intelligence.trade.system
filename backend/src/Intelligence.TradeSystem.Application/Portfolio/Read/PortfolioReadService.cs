using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed class PortfolioReadService(
    IPortfolioReadStore store,
    RealtimeObservationReadService realtimeObservationReadService,
    IPositionReadStore positionReadStore,
    MarketMarkPriceService marketMarkPriceService,
    TimeProvider timeProvider)
{
    public async Task<PortfolioReadResult> GetLatestAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        var result = await store
            .GetLatestAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.AccountExists || result.Summary is not { } summary)
        {
            return result;
        }

        var observation = await realtimeObservationReadService
            .GetAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var positions = await positionReadStore
            .GetMarketValuationInputsByAccountAsync(userId, exchangeAccountId, cancellationToken)
            .ConfigureAwait(false);
        var currentPositionIds = positions.Select(position => position.Id).ToArray();
        var snapshotPositionIds = summary.SnapshotPositionIds;
        var positionSetsMatch = snapshotPositionIds is not null
            && snapshotPositionIds.Count == currentPositionIds.Length
            && snapshotPositionIds.Distinct().Count() == snapshotPositionIds.Count
            && currentPositionIds.Distinct().Count() == currentPositionIds.Length
            && snapshotPositionIds.ToHashSet().SetEquals(currentPositionIds);
        var compositionMayBeIncomplete =
            observation.RecoveryRequired
            || observation.Status is
                RealtimeObservationReadStatus.Degraded
                or RealtimeObservationReadStatus.Reconnecting;
        var compositionConfirmed =
            summary.PositionsFullyReconciled
            && positionSetsMatch
            && !compositionMayBeIncomplete;
        var valuationFreshness = await marketMarkPriceService
            .GetPortfolioFreshnessAsync(
                positions,
                compositionConfirmed,
                cancellationToken)
            .ConfigureAwait(false);
        var factualSummary = snapshotPositionIds is not null && !positionSetsMatch
            ? summary with { PositionsFullyReconciled = false }
            : summary;
        var factualFreshness = RealtimeFreshnessProjection.ProjectFactualFreshness(
            factualSummary,
            observation,
            timeProvider.GetUtcNow());

        return result with
        {
            Summary = summary with
            {
                FactualFreshness = factualFreshness,
                ValuationFreshness = valuationFreshness,
            },
        };
    }
}
