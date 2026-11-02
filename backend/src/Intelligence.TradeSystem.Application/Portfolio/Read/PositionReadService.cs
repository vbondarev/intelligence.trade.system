using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed class PositionReadService(
    IPositionReadStore store,
    MarketMarkPriceService marketMarkPriceService)
{
    public async Task<PositionReadPage> ListAsync(
        UserId userId,
        PositionReadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var page = await store.ListAsync(userId, query, cancellationToken).ConfigureAwait(false);
        if (page.Items.Count == 0)
        {
            return page;
        }

        var valuations = await marketMarkPriceService
            .GetForPositionsAsync(page.Items, cancellationToken)
            .ConfigureAwait(false);
        return page with
        {
            Items = page.Items
                .Select(item => item with { MarketValuation = valuations[item.Id] })
                .ToArray(),
        };
    }

    public async Task<PositionReadDetail?> GetByIdAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        var detail = await store
            .GetByIdAsync(userId, positionId, cancellationToken)
            .ConfigureAwait(false);
        if (detail is null)
        {
            return null;
        }

        var valuations = await marketMarkPriceService
            .GetForPositionsAsync([detail.ListItem], cancellationToken)
            .ConfigureAwait(false);
        return detail with
        {
            ListItem = detail.ListItem with { MarketValuation = valuations[detail.ListItem.Id] },
        };
    }
}
