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
        var valuationFreshness = await marketMarkPriceService
            .GetPortfolioFreshnessAsync(
                positions,
                summary.PositionsFullyReconciled,
                cancellationToken)
            .ConfigureAwait(false);
        var factualFreshness = RealtimeFreshnessProjection.ProjectFactualFreshness(
            summary,
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
