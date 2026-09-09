using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

internal static class ApplicationEventOutboxTelemetry
{
    public const string ActivitySourceName =
        "Intelligence.TradeSystem.Infrastructure.ApplicationEvents";
    public const string MeterName = ActivitySourceName;
    public const string WorkerName = "application-event-outbox-dispatcher";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Claimed =
        Meter.CreateCounter<long>("application_event_outbox.claimed", "{event}");
    private static readonly Counter<long> Processed =
        Meter.CreateCounter<long>("application_event_outbox.processed", "{event}");
    private static readonly Counter<long> Failed =
        Meter.CreateCounter<long>("application_event_outbox.failed", "{event}");
    private static readonly Counter<long> Retried =
        Meter.CreateCounter<long>("application_event_outbox.retried", "{event}");
    private static readonly Histogram<double> DispatchDuration =
        Meter.CreateHistogram<double>("application_event_outbox.dispatch_duration", "ms");
    private static readonly Histogram<double> OldestPendingAge =
        Meter.CreateHistogram<double>("application_event_outbox.oldest_pending_age", "s");
    private static readonly KeyValuePair<string, object?>[] BaseTags =
        [new("dispatcher", WorkerName)];

    public static Activity? StartDispatchActivity(string eventType)
    {
        var activity = ActivitySource.StartActivity(
            "application_event.outbox.dispatch",
            ActivityKind.Internal);
        activity?.SetTag("event_type", eventType);
        return activity;
    }

    public static void RecordClaimed(
        int count,
        DateTimeOffset now,
        DateTimeOffset? oldestCreatedAt)
    {
        Claimed.Add(count, BaseTags);
        if (oldestCreatedAt is { } createdAt)
        {
            OldestPendingAge.Record(
                Math.Max(0, (now - createdAt).TotalSeconds),
                BaseTags);
        }
    }

    public static void RecordProcessed(string eventType) =>
        Processed.Add(1, Tags(eventType, "processed"));

    public static void RecordFailed(string eventType) =>
        Failed.Add(1, Tags(eventType, "failed"));

    public static void RecordRetry(string eventType) =>
        Retried.Add(1, Tags(eventType, "retry"));

    public static void RecordDispatchDuration(
        string eventType,
        TimeSpan duration,
        string outcome) =>
        DispatchDuration.Record(
            duration.TotalMilliseconds,
            Tags(eventType, outcome));

    private static KeyValuePair<string, object?>[] Tags(
        string eventType,
        string outcome) =>
    [
        new("dispatcher", WorkerName),
        new("event_type", eventType),
        new("outcome", outcome),
    ];
}
