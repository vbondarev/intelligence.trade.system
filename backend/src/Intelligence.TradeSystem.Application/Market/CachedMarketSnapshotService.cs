using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Разделяет готовые публичные снимки, оставляя их построение в builder.
/// </summary>
public sealed class CachedMarketSnapshotService : IMarketSnapshotService
{
    private readonly IPublicMarketSnapshotCache _cache;

    public CachedMarketSnapshotService(IPublicMarketSnapshotCache cache)
    {
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
            cancellationToken);
    }
}
