using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Не зависящий от фреймворка порт кэша для готовых публичных рыночных снимков.
/// </summary>
public interface IPublicMarketSnapshotCache
{
    ValueTask<MarketSnapshot> GetOrCreateAsync(
        PublicMarketSnapshotCacheKey key,
        CancellationToken cancellationToken = default);
}
