using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class RealtimeObservationProcessingTelemetryTests
{
    [Fact]
    public void Processing_metrics_use_bounded_outcomes_and_do_not_publish_identifiers()
    {
        var capture = new AsyncLocal<bool> { Value = true };
        var measurements = new ConcurrentQueue<(string Name, Dictionary<string, string?> Tags)>();
        var discards = new List<(long Count, string? Unit, string? Reason)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == RealtimeObservationProcessingTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, count, tags, _) =>
        {
            if (capture.Value)
            {
                var dimensions = ToDictionary(tags);
                measurements.Enqueue((instrument.Name, dimensions));
                if (instrument.Name == "exchange.realtime.observations.queue.discarded")
                {
                    Assert.Single(dimensions);
                    discards.Add((count, instrument.Unit, dimensions["reason"]));
                }
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (capture.Value)
            {
                measurements.Enqueue((instrument.Name, ToDictionary(tags)));
            }
        });
        listener.Start();

        var completedAt = new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        RealtimeObservationProcessingTelemetry.ObservationProcessed(
            isPosition: true,
            RealtimeObservationApplyOutcome.Applied,
            TimeSpan.FromMilliseconds(8),
            completedAt.AddSeconds(-1),
            completedAt.AddMilliseconds(-500),
            completedAt);
        RealtimeObservationProcessingTelemetry.ObservationFailed(
            isPosition: false,
            TimeSpan.FromMilliseconds(4));
        RealtimeObservationProcessingTelemetry.ObservationProcessed(
            isPosition: false,
            RealtimeObservationApplyOutcome.Applied,
            TimeSpan.FromMilliseconds(5),
            messageCreatedAt: null,
            receivedAt: completedAt.AddMilliseconds(10),
            completedAt: completedAt);
        RealtimeObservationProcessingTelemetry.ReconciliationCompleted(
            TimeSpan.FromMilliseconds(12),
            "recovery",
            "committed");
        RealtimeObservationProcessingTelemetry.QueueDiscarded(0, "recovery_required");
        RealtimeObservationProcessingTelemetry.QueueDiscarded(2, "recovery_required");
        RealtimeObservationProcessingTelemetry.QueueDiscarded(1, "generation_superseded");
        RealtimeObservationProcessingTelemetry.QueueDiscarded(3, "account_unavailable");

        Assert.Equal(
            [(2L, "{observation}", "recovery_required"), (1L, "{observation}", "generation_superseded"),
                (3L, "{observation}", "account_unavailable")],
            discards);

        var capturedMeasurements = measurements.ToArray();
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.observations.apply.outcomes"
            && HasTag(measurement.Tags, "stream", "position")
            && HasTag(measurement.Tags, "outcome", "applied"));
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.observations.creation_to_commit.duration"
            && HasTag(measurement.Tags, "stream", "position"));
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.observations.creation_time_unavailable"
            && HasTag(measurement.Tags, "stream", "wallet"));
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.observations.receive_time_unavailable"
            && HasTag(measurement.Tags, "stream", "wallet"));
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.observations.apply.outcomes"
            && HasTag(measurement.Tags, "stream", "wallet")
            && HasTag(measurement.Tags, "outcome", "failed"));
        capturedMeasurements.Should().Contain(measurement =>
            measurement.Name == "exchange.realtime.reconciliation.duration"
            && HasTag(measurement.Tags, "kind", "recovery")
            && HasTag(measurement.Tags, "outcome", "committed"));
        var tagKeys = capturedMeasurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();
        foreach (var forbiddenKey in new[]
                 {
                     "userId",
                     "user_id",
                     "exchangeAccountId",
                     "exchange_account_id",
                     "symbol",
                     "exception",
                     "credentials",
                 })
        {
            tagKeys.Should().NotContain(forbiddenKey);
        }
    }

    private static Dictionary<string, string?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());

    private static bool HasTag(
        Dictionary<string, string?> tags,
        string key,
        string expectedValue) =>
        tags.TryGetValue(key, out var actualValue) && actualValue == expectedValue;
}
