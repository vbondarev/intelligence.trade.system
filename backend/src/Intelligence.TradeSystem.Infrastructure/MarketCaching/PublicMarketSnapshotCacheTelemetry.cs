using System.Diagnostics;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Market;

namespace Intelligence.TradeSystem.Infrastructure.MarketCaching;

internal static class PublicMarketSnapshotCacheTelemetry
{
    public const string ActivitySourceName =
        "Intelligence.TradeSystem.Infrastructure.MarketCaching";
    public const string MeterName = ActivitySourceName;

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Requests =
        Meter.CreateCounter<long>("market_snapshot_cache.requests", "{request}");
    private static readonly Counter<long> SourceBuilds =
        Meter.CreateCounter<long>("market_snapshot_cache.source_builds", "{build}");
    private static readonly Counter<long> SourceBuildFailures =
        Meter.CreateCounter<long>("market_snapshot_cache.source_build_failures", "{failure}");
    private static readonly Histogram<double> SourceBuildDuration =
        Meter.CreateHistogram<double>("market_snapshot_cache.source_build_duration", "ms");

    public static Activity? StartRequestActivity(PublicMarketSnapshotCacheKey key)
    {
        var activity = ActivitySource.StartActivity("market.snapshot.cache.get", ActivityKind.Internal);
        activity?.SetTag("market.exchange", key.ExchangeId.ToString());
        activity?.SetTag("market.category", key.Category.ToString());
        return activity;
    }

    public static void RecordRequest(PublicMarketSnapshotCacheKey key) => Requests.Add(1, Tags(key));

    public static void RecordSourceBuildStarted(PublicMarketSnapshotCacheKey key) =>
        SourceBuilds.Add(1, Tags(key));

    public static void RecordSourceBuildCompleted(
        PublicMarketSnapshotCacheKey key,
        TimeSpan duration,
        string outcome) =>
        SourceBuildDuration.Record(
            duration.TotalMilliseconds,
            Tags(key, outcome));

    public static void RecordSourceBuildFailure(PublicMarketSnapshotCacheKey key) =>
        SourceBuildFailures.Add(1, Tags(key, "failure"));

    public static KeyValuePair<string, object?>[] Tags(
        PublicMarketSnapshotCacheKey key,
        string? outcome = null) =>
        outcome is null
            ?
            [
                new("exchange", key.ExchangeId.ToString()),
                new("category", key.Category.ToString()),
            ]
            :
            [
                new("exchange", key.ExchangeId.ToString()),
                new("category", key.Category.ToString()),
                new("outcome", outcome),
            ];
}
