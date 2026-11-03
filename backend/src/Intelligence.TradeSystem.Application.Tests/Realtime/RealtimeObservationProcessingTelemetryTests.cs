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
        var measurements = new ConcurrentQueue<(string Name, Dictionary<string, string?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == RealtimeObservationProcessingTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Enqueue((instrument.Name, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Enqueue((instrument.Name, ToDictionary(tags))));
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
