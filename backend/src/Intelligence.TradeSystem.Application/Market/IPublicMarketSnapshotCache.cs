using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Framework-neutral cache port for completed public market snapshots.
/// </summary>
public interface IPublicMarketSnapshotCache
{
    ValueTask<MarketSnapshot> GetOrCreateAsync(
        PublicMarketSnapshotCacheKey key,
        Func<CancellationToken, ValueTask<MarketSnapshot>> factory,
        CancellationToken cancellationToken = default);
}
