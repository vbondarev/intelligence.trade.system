using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class CompositionRootSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CompositionRootSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Program_CompositionRoot_Resolves_Core_Api_Services()
    {
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        serviceProvider.GetRequiredService<IBybitRestClient>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IMarketDataProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IDerivativesDataProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IPrivateAccountProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IBybitProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IPublicMarketDataCollector>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IMarketSnapshotService>().Should().NotBeNull();
    }
}
