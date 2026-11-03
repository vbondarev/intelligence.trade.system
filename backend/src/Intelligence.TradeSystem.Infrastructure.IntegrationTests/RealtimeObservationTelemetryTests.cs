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
        var measurements = new List<(string Name, Dictionary<string, string?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ExchangeRealtimeObservationTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, ToDictionary(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Add((instrument.Name, ToDictionary(tags))));
        listener.Start();

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

        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.providers.active");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.observations.received"
            && measurement.Tags["stream"] == "position");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.observations.received"
            && measurement.Tags["stream"] == "wallet");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.transport.status_updates"
            && measurement.Tags["status"] == "degraded");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.public_subscription.start_unconfirmed");
        Assert.Contains(measurements, measurement =>
            measurement.Name == "exchange.realtime.public_subscription.dispose_unconfirmed");
        var tagKeys = measurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();
        Assert.DoesNotContain(tagKeys, key => key is
            "userId" or "user_id" or "exchangeAccountId" or "exchange_account_id"
            or "symbol" or "exception" or "credentials");
    }

    private static Dictionary<string, string?> ToDictionary(
        ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value?.ToString());
}
