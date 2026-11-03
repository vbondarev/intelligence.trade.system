using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Infrastructure.Realtime;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed class RealtimeObservationTelemetryTests
{
    [Fact]
    public void Realtime_metrics_use_low_cardinality_tags_and_balance_active_counts()
    {
        var measurements = new ConcurrentQueue<MetricMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ExchangeRealtimeObservationTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue(new MetricMeasurement(
                instrument.Name,
                value,
                null,
                ToDictionary(tags),
                Environment.CurrentManagedThreadId)));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue(new MetricMeasurement(
                instrument.Name,
                null,
                value,
                ToDictionary(tags),
                Environment.CurrentManagedThreadId)));
        listener.Start();

        var recordingThreadId = Environment.CurrentManagedThreadId;
        ExchangeRealtimeObservationTelemetry.RecordProviderSessionStarted();
        ExchangeRealtimeObservationTelemetry.RecordPositionObservationReceived();
        ExchangeRealtimeObservationTelemetry.RecordWalletObservationReceived();
        ExchangeRealtimeObservationTelemetry.RecordTransportStatus(
            RealtimeObservationTransportStatus.Degraded);
        ExchangeRealtimeObservationTelemetry.RecordPublicSubscriptionStartUnconfirmed();
        ExchangeRealtimeObservationTelemetry.RecordPublicSubscriptionDisposeUnconfirmed();
        ExchangeRealtimeObservationTelemetry.RecordProtectedPublicSubscription();
        ExchangeRealtimeObservationTelemetry.RecordProtectedPublicSubscriptionReleased();
        ExchangeRealtimeObservationTelemetry.RecordProviderStopped();

        var testMeasurements = measurements
            .Where(measurement => measurement.ThreadId == recordingThreadId)
            .ToArray();
        var activeProviderMeasurements = testMeasurements
            .Where(measurement => measurement.Name == "exchange.realtime.providers.active")
            .Select(measurement => Assert.IsType<long>(measurement.LongValue))
            .ToArray();
        Assert.Equal(new long[] { 1L, -1L }, activeProviderMeasurements);
        Assert.Equal(0L, activeProviderMeasurements.Sum());
        var protectedSubscriptionMeasurements = testMeasurements
            .Where(measurement =>
                measurement.Name == "exchange.realtime.public_subscription.protected_degraded")
            .Select(measurement => Assert.IsType<long>(measurement.LongValue))
            .ToArray();
        Assert.Equal(new long[] { 1L, -1L }, protectedSubscriptionMeasurements);
        Assert.Equal(0L, protectedSubscriptionMeasurements.Sum());
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.providers.active");
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.observations.received"
            && measurement.Tags["stream"] == "position");
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.observations.received"
            && measurement.Tags["stream"] == "wallet");
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.transport.status_updates"
            && measurement.Tags["status"] == "degraded");
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.public_subscription.start_unconfirmed");
        Assert.Contains(testMeasurements, measurement =>
            measurement.Name == "exchange.realtime.public_subscription.dispose_unconfirmed");
        var tagKeys = testMeasurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();
        Assert.DoesNotContain(tagKeys, key => key is
            "userId" or "user_id" or "exchangeAccountId" or "exchange_account_id"
            or "symbol" or "exception" or "credentials");
    }

    private sealed record MetricMeasurement(
        string Name,
        long? LongValue,
        double? DoubleValue,
        Dictionary<string, string?> Tags,
        int ThreadId);

    private static Dictionary<string, string?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
}
