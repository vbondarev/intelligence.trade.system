using Intelligence.TradeSystem.Infrastructure;
using Intelligence.TradeSystem.Infrastructure.ApplicationEvents;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
}
