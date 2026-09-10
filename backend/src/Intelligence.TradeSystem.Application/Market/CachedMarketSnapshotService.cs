using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Shares completed public snapshots while keeping snapshot construction in the builder.
/// </summary>
public sealed class CachedMarketSnapshotService : IMarketSnapshotService
{
    private readonly MarketSnapshotService _snapshotBuilder;
    private readonly IPublicMarketSnapshotCache _cache;

    public CachedMarketSnapshotService(
        MarketSnapshotService snapshotBuilder,
        IPublicMarketSnapshotCache cache)
    {
        _snapshotBuilder = snapshotBuilder;
        _cache = cache;
    }

    public async Task<MarketSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        var key = PublicMarketSnapshotCacheKey.Create(exchangeId, symbol, category);

        return await _cache.GetOrCreateAsync(
            key,
            cacheCancellationToken => new ValueTask<MarketSnapshot>(
                _snapshotBuilder.BuildSnapshotAsync(
                    key.ExchangeId,
                    key.Symbol,
                    key.Category,
                    cacheCancellationToken)),
            cancellationToken);
    }
}
