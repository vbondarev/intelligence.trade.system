using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

internal static class ExchangeRealtimeObservationTelemetry
{
    public const string ActivitySourceName = "Intelligence.TradeSystem.Infrastructure.Realtime";
    public const string MeterName = ActivitySourceName;
    private const string WorkerName = "exchange-realtime-observation";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> DiscoveryPasses = Meter.CreateCounter<long>(
        "exchange.realtime.discovery.passes",
        unit: "{pass}");
    private static readonly Counter<long> ProvidersStarted = Meter.CreateCounter<long>(
        "exchange.realtime.providers.started",
        unit: "{provider}");
    private static readonly Counter<long> ProvidersStopped = Meter.CreateCounter<long>(
        "exchange.realtime.providers.stopped",
        unit: "{provider}");
    private static readonly Counter<long> ObservationSignalsDropped = Meter.CreateCounter<long>(
        "exchange.realtime.observation.signals.dropped",
        unit: "{signal}");
    private static readonly Counter<long> RecoveriesRequested = Meter.CreateCounter<long>(
        "exchange.realtime.recoveries.requested",
        unit: "{recovery}");
    private static readonly Counter<long> HeartbeatsWritten = Meter.CreateCounter<long>(
        "exchange.realtime.heartbeats.written",
        unit: "{heartbeat}");
    private static readonly Histogram<double> DiscoveryDuration = Meter.CreateHistogram<double>(
        "exchange.realtime.discovery.duration",
        unit: "ms");
    private static readonly KeyValuePair<string, object?>[] BaseTags =
    [
        new("worker", WorkerName),
        new("exchange", "Bybit"),
    ];

    public static Activity? StartDiscoveryActivity()
    {
        var activity = ActivitySource.StartActivity(
            "exchange.realtime.discovery",
            ActivityKind.Internal);
        activity?.SetTag("worker", WorkerName);
        activity?.SetTag("exchange", "Bybit");
        return activity;
    }

    public static void RecordDiscovery(TimeSpan duration) =>
        RecordDuration(DiscoveryPasses, DiscoveryDuration, duration);

    public static void RecordProviderStarted() => ProvidersStarted.Add(1, BaseTags);

    public static void RecordProviderStopped() => ProvidersStopped.Add(1, BaseTags);

    public static void RecordDroppedSignal() => ObservationSignalsDropped.Add(1, BaseTags);

    public static void RecordRecoveryRequested(long count) =>
        RecoveriesRequested.Add(count, BaseTags);

    public static void RecordHeartbeat() => HeartbeatsWritten.Add(1, BaseTags);

    private static void RecordDuration(
        Counter<long> counter,
        Histogram<double> histogram,
        TimeSpan duration)
    {
        counter.Add(1, BaseTags);
        histogram.Record(duration.TotalMilliseconds, BaseTags);
    }
}
