using System.Diagnostics;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;

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
    private static readonly UpDownCounter<long> ActiveProviders = Meter.CreateUpDownCounter<long>(
        "exchange.realtime.providers.active",
        unit: "{provider}");
    private static readonly Counter<long> TransportStatusUpdates = Meter.CreateCounter<long>(
        "exchange.realtime.transport.status_updates",
        unit: "{update}");
    private static readonly Counter<long> TransportGapSignals = Meter.CreateCounter<long>(
        "exchange.realtime.transport.gap_signals",
        unit: "{signal}");
    private static readonly Counter<long> ObservationsReceived = Meter.CreateCounter<long>(
        "exchange.realtime.observations.received",
        unit: "{observation}");
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
    private static readonly Counter<long> PublicSubscriptionStartUnconfirmed = Meter.CreateCounter<long>(
        "exchange.realtime.public_subscription.start_unconfirmed",
        unit: "{subscription}");
    private static readonly Counter<long> PublicSubscriptionDisposeUnconfirmed = Meter.CreateCounter<long>(
        "exchange.realtime.public_subscription.dispose_unconfirmed",
        unit: "{subscription}");
    private static readonly UpDownCounter<long> ProtectedPublicSubscriptions = Meter.CreateUpDownCounter<long>(
        "exchange.realtime.public_subscription.protected_degraded",
        unit: "{subscription}");
    private static readonly KeyValuePair<string, object?>[] BaseTags =
    [
        new("worker", WorkerName),
        new("exchange", "Bybit"),
    ];
    private static readonly KeyValuePair<string, object?>[] PositionObservationTags =
    [
        new("worker", WorkerName),
        new("exchange", "Bybit"),
        new("stream", "position"),
    ];
    private static readonly KeyValuePair<string, object?>[] WalletObservationTags =
    [
        new("worker", WorkerName),
        new("exchange", "Bybit"),
        new("stream", "wallet"),
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

    public static void RecordProviderStarted()
    {
        ProvidersStarted.Add(1, BaseTags);
    }

    public static void RecordProviderSessionStarted() => ActiveProviders.Add(1, BaseTags);

    public static void RecordProviderStopped()
    {
        ProvidersStopped.Add(1, BaseTags);
        ActiveProviders.Add(-1, BaseTags);
    }

    public static void RecordTransportStatus(RealtimeObservationTransportStatus status)
    {
        var tags = new TagList
        {
            { "worker", WorkerName },
            { "exchange", "Bybit" },
            { "status", ToTransportStatusTag(status) },
        };
        TransportStatusUpdates.Add(1, tags);
    }

    public static void RecordTransportGapSignal(RealtimeTransportStatus status)
    {
        var reason = status switch
        {
            RealtimeTransportStatus.ConnectionLost => "connection_lost",
            RealtimeTransportStatus.MessageRejected => "message_rejected",
            _ => null,
        };
        if (reason is null)
        {
            return;
        }

        var tags = new TagList
        {
            { "worker", WorkerName },
            { "exchange", "Bybit" },
            { "reason", reason },
        };
        TransportGapSignals.Add(1, tags);
    }

    public static void RecordPositionObservationReceived() =>
        ObservationsReceived.Add(1, PositionObservationTags);

    public static void RecordWalletObservationReceived() =>
        ObservationsReceived.Add(1, WalletObservationTags);

    public static void RecordDroppedSignal() => ObservationSignalsDropped.Add(1, BaseTags);

    public static void RecordRecoveryRequested(long count) =>
        RecoveriesRequested.Add(count, BaseTags);

    public static void RecordHeartbeat() => HeartbeatsWritten.Add(1, BaseTags);

    public static void RecordPublicSubscriptionStartUnconfirmed() =>
        PublicSubscriptionStartUnconfirmed.Add(1, BaseTags);

    public static void RecordPublicSubscriptionDisposeUnconfirmed() =>
        PublicSubscriptionDisposeUnconfirmed.Add(1, BaseTags);

    public static void RecordProtectedPublicSubscription() =>
        ProtectedPublicSubscriptions.Add(1, BaseTags);

    public static void RecordProtectedPublicSubscriptionReleased() =>
        ProtectedPublicSubscriptions.Add(-1, BaseTags);

    private static string ToTransportStatusTag(RealtimeObservationTransportStatus status) => status switch
    {
        RealtimeObservationTransportStatus.Initializing => "initializing",
        RealtimeObservationTransportStatus.Connected => "connected",
        RealtimeObservationTransportStatus.Reconnecting => "reconnecting",
        RealtimeObservationTransportStatus.Degraded => "degraded",
        RealtimeObservationTransportStatus.Stopped => "stopped",
        _ => throw new NotSupportedException($"Transport status '{status}' is not supported."),
    };

    private static void RecordDuration(
        Counter<long> counter,
        Histogram<double> histogram,
        TimeSpan duration)
    {
        counter.Add(1, BaseTags);
        histogram.Record(duration.TotalMilliseconds, BaseTags);
    }
}
