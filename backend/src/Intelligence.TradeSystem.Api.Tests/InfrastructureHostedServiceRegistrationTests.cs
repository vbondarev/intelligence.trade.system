using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Infrastructure;
using Intelligence.TradeSystem.Infrastructure.ApplicationEvents;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Intelligence.TradeSystem.Infrastructure.Realtime;
using Intelligence.TradeSystem.ServiceDefaults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class InfrastructureHostedServiceRegistrationTests
{
    [Fact]
    public void Background_synchronization_registers_services_without_trade_system_connection_string()
    {
        var services = new ServiceCollection();

        services.AddExchangeAccountBackgroundSynchronization(CreateConfiguration());

        services.Should().ContainSingle(
            descriptor => descriptor.ServiceType == typeof(IExchangeAccountBackgroundSyncSweep));
        services.Should().ContainSingle(
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ExchangeAccountBackgroundSyncWorker));
    }

    [Fact]
    public void Realtime_observation_registers_supervisor_and_worker_without_trade_system_connection_string()
    {
        var services = new ServiceCollection();

        services.AddExchangeRealtimeObservation(CreateConfiguration());

        services.Should().ContainSingle(
            descriptor => descriptor.ServiceType == typeof(ExchangeRealtimeObservationSupervisor));
        services.Should().ContainSingle(
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ExchangeRealtimeObservationWorker));
    }

    [Fact]
    public void Realtime_observation_rejects_non_finite_reconnect_jitter()
    {
        var result = new ExchangeRealtimeObservationOptionsValidator().Validate(
            name: null,
            new ExchangeRealtimeObservationOptions { ReconnectJitter = double.NaN });

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task Service_defaults_subscribes_to_realtime_meter_and_activity_source()
    {
        const string telemetryName = "Intelligence.TradeSystem.Infrastructure.Realtime";
        const string metricName = "test.realtime.telemetry.registration";
        var exporter = new CapturingMetricExporter();
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
                metrics.AddReader(new PeriodicExportingMetricReader(exporter)));

        using var host = builder.Build();
        await host.StartAsync();

        using var meter = new Meter(telemetryName);
        meter.CreateCounter<long>(metricName).Add(1);
        using var activitySource = new ActivitySource(telemetryName);
        using var activity = activitySource.StartActivity("test.realtime.telemetry.registration");

        Assert.NotNull(activity);
        var meterProvider = host.Services.GetRequiredService<MeterProvider>();
        Assert.True(meterProvider.ForceFlush());
        Assert.Contains(metricName, exporter.MetricNames);

        await host.StopAsync();
    }

    [Fact]
    public void Outbox_dispatcher_registers_hosted_service_without_trade_system_connection_string()
    {
        var services = new ServiceCollection();

        services.AddApplicationEventOutboxDispatcher(CreateConfiguration());

        services.Should().ContainSingle(
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(ApplicationEventOutboxDispatcherWorker));
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ExchangeAccountBackgroundSync:Enabled",
                    "false"),
                new KeyValuePair<string, string?>(
                    "ApplicationEventOutboxDispatcher:Enabled",
                    "false"),
            ])
            .Build();

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        private readonly ConcurrentQueue<string> metricNames = new();

        public IReadOnlyCollection<string> MetricNames => metricNames.ToArray();

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                metricNames.Enqueue(metric.Name);
            }

            return ExportResult.Success;
        }
    }
}
