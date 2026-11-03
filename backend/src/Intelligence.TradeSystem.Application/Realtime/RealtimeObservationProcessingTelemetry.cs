using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Intelligence.TradeSystem.Application.Realtime;

internal static class RealtimeObservationProcessingTelemetry
{
    public const string MeterName = "Intelligence.TradeSystem.Application.Realtime";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> ApplyOutcomes = Meter.CreateCounter<long>(
        "exchange.realtime.observations.apply.outcomes",
        unit: "{observation}");
    private static readonly Histogram<double> ApplyDuration = Meter.CreateHistogram<double>(
        "exchange.realtime.observations.apply.duration",
        unit: "ms");
    private static readonly Histogram<double> ReceiveToCommitDuration = Meter.CreateHistogram<double>(
        "exchange.realtime.observations.receive_to_commit.duration",
        unit: "ms");
    private static readonly Histogram<double> CreationToCommitDuration = Meter.CreateHistogram<double>(
        "exchange.realtime.observations.creation_to_commit.duration",
        unit: "ms");
    private static readonly Counter<long> UnmeasurableAppliedObservations = Meter.CreateCounter<long>(
        "exchange.realtime.observations.creation_time_unavailable",
        unit: "{observation}");
    private static readonly Counter<long> UnmeasurableReceiveTimestamps = Meter.CreateCounter<long>(
        "exchange.realtime.observations.receive_time_unavailable",
        unit: "{observation}");
    private static readonly Histogram<double> ReconciliationDuration = Meter.CreateHistogram<double>(
        "exchange.realtime.reconciliation.duration",
        unit: "ms");

    public static void ObservationProcessed(
        bool isPosition,
        RealtimeObservationApplyOutcome outcome,
        TimeSpan processingDuration,
        DateTimeOffset? messageCreatedAt,
        DateTimeOffset receivedAt,
        DateTimeOffset completedAt)
    {
        var stream = isPosition ? "position" : "wallet";
        var outcomeName = ToOutcomeTag(outcome);
        var tags = new TagList
        {
            { "stream", stream },
            { "outcome", outcomeName },
        };
        ApplyOutcomes.Add(1, tags);
        ApplyDuration.Record(processingDuration.TotalMilliseconds, tags);

        if (outcome == RealtimeObservationApplyOutcome.Applied)
        {
            RecordReceiveToCommit(stream, receivedAt, completedAt);
            RecordCreationToCommit(stream, messageCreatedAt, completedAt);
        }
    }

    public static void ObservationFailed(
        bool isPosition,
        TimeSpan processingDuration)
    {
        var stream = isPosition ? "position" : "wallet";
        var tags = new TagList
        {
            { "stream", stream },
            { "outcome", "failed" },
        };
        ApplyOutcomes.Add(1, tags);
        ApplyDuration.Record(processingDuration.TotalMilliseconds, tags);
    }

    public static void ReconciliationCompleted(
        TimeSpan duration,
        string kind,
        string outcome)
    {
        var tags = new TagList
        {
            { "kind", kind },
            { "outcome", outcome },
        };
        ReconciliationDuration.Record(duration.TotalMilliseconds, tags);
    }

    private static void RecordReceiveToCommit(
        string stream,
        DateTimeOffset receivedAt,
        DateTimeOffset completedAt)
    {
        if (receivedAt == default || completedAt < receivedAt)
        {
            var unavailableTags = new TagList { { "stream", stream } };
            UnmeasurableReceiveTimestamps.Add(1, unavailableTags);
            return;
        }

        var tags = new TagList { { "stream", stream } };
        ReceiveToCommitDuration.Record((completedAt - receivedAt).TotalMilliseconds, tags);
    }

    private static void RecordCreationToCommit(
        string stream,
        DateTimeOffset? messageCreatedAt,
        DateTimeOffset completedAt)
    {
        if (messageCreatedAt is not { } createdAt || createdAt > completedAt)
        {
            var tags = new TagList { { "stream", stream } };
            UnmeasurableAppliedObservations.Add(1, tags);
            return;
        }

        var durationTags = new TagList { { "stream", stream } };
        CreationToCommitDuration.Record((completedAt - createdAt).TotalMilliseconds, durationTags);
    }

    private static string ToOutcomeTag(RealtimeObservationApplyOutcome outcome) => outcome switch
    {
        RealtimeObservationApplyOutcome.Applied => "applied",
        RealtimeObservationApplyOutcome.Duplicate => "duplicate",
        RealtimeObservationApplyOutcome.Stale => "stale",
        RealtimeObservationApplyOutcome.Ignored => "ignored",
        RealtimeObservationApplyOutcome.RecoveryRequired => "recovery_required",
        RealtimeObservationApplyOutcome.GenerationSuperseded => "generation_superseded",
        RealtimeObservationApplyOutcome.AccountUnavailable => "account_unavailable",
        _ => throw new NotSupportedException($"Observation outcome '{outcome}' is not supported."),
    };
}
