using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
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
    public async Task Program_CompositionRoot_Resolves_Core_Api_Services()
    {
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        serviceProvider.GetRequiredService<IBybitRestClient>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IMarketDataProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IDerivativesDataProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<BybitPrivateAccountProviderFactory>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IExchangeAccountAccessVerifier>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IPublicMarketDataCollector>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IMarketSnapshotService>()
            .Should()
            .BeOfType<CachedMarketSnapshotService>();
        serviceProvider.GetRequiredService<IRecommendationPolicyDefinitionProvider>().Should().NotBeNull();
        serviceProvider.GetRequiredService<RecommendationStabilityPolicy>().Should().NotBeNull();
        serviceProvider.GetService<RecommendationService>().Should().BeNull();
        var definition = await serviceProvider
            .GetRequiredService<IRecommendationPolicyDefinitionProvider>()
            .GetAsync();
        definition.Identity.Should().Be(PolicyDefinition.Default.Identity);
    }

    [Fact]
    public void Program_CompositionRoot_Registers_A_Handler_For_Every_Persisted_Application_Event()
    {
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        AssertHandler<PositionOpenedEventV1>(serviceProvider);
        AssertHandler<PositionChangedEventV1>(serviceProvider);
        AssertHandler<PositionClosedEventV1>(serviceProvider);
        AssertHandler<ExchangeAccountSyncDegradedEventV1>(serviceProvider);
        AssertHandler<ExchangeAccountUpdatedEventV1>(serviceProvider);
        AssertHandler<PortfolioUpdatedEventV1>(serviceProvider);
        AssertHandler<PositionEvaluationUpdatedEventV1>(serviceProvider);
    }

    private static void AssertHandler<TEvent>(IServiceProvider serviceProvider)
        where TEvent : IApplicationEvent
    {
        serviceProvider
            .GetServices<IApplicationEventHandler<TEvent>>()
            .Should()
            .ContainSingle();
    }
}
