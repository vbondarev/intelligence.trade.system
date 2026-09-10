using System.Diagnostics;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.MarketCaching;

public sealed class PublicMarketSnapshotCache : IPublicMarketSnapshotCache
{
    private readonly HybridCache _cache;
    private readonly IOptions<PublicMarketSnapshotCacheOptions> _options;

    public PublicMarketSnapshotCache(
        HybridCache cache,
        IOptions<PublicMarketSnapshotCacheOptions> options)
    {
        _cache = cache;
        _options = options;
    }

    public async ValueTask<MarketSnapshot> GetOrCreateAsync(
        PublicMarketSnapshotCacheKey key,
        Func<CancellationToken, ValueTask<MarketSnapshot>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        PublicMarketSnapshotCacheTelemetry.RecordRequest(key);
        using var activity = PublicMarketSnapshotCacheTelemetry.StartRequestActivity(key);

        try
        {
            var options = _options.Value;
            var result = options.Enabled
                ? await _cache.GetOrCreateAsync(
                    key.ToStableCacheKey(),
                    cacheCancellationToken => BuildFromSourceAsync(
                        key,
                        factory,
                        cacheCancellationToken),
                    new HybridCacheEntryOptions
                    {
                        Expiration = options.EntryLifetime,
                        LocalCacheExpiration = options.EntryLifetime,
                    },
                    cancellationToken: cancellationToken)
                : await BuildFromSourceAsync(key, factory, cancellationToken);

            activity?.SetTag("cache.outcome", "success");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("cache.outcome", "cancelled");
            throw;
        }
        catch
        {
            activity?.SetTag("cache.outcome", "failure");
            throw;
        }
    }

    private static async ValueTask<MarketSnapshot> BuildFromSourceAsync(
        PublicMarketSnapshotCacheKey key,
        Func<CancellationToken, ValueTask<MarketSnapshot>> factory,
        CancellationToken cancellationToken)
    {
        PublicMarketSnapshotCacheTelemetry.RecordSourceBuildStarted(key);
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            var snapshot = await factory(cancellationToken);
            PublicMarketSnapshotCacheTelemetry.RecordSourceBuildCompleted(
                key,
                Stopwatch.GetElapsedTime(startedAt),
                "success");
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            PublicMarketSnapshotCacheTelemetry.RecordSourceBuildCompleted(
                key,
                Stopwatch.GetElapsedTime(startedAt),
                "cancelled");
            throw;
        }
        catch
        {
            PublicMarketSnapshotCacheTelemetry.RecordSourceBuildFailure(key);
            PublicMarketSnapshotCacheTelemetry.RecordSourceBuildCompleted(
                key,
                Stopwatch.GetElapsedTime(startedAt),
                "failure");
            throw;
        }
    }
}
