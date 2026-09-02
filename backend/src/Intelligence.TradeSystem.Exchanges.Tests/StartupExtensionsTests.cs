using Bybit.Net.Interfaces.Clients;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class StartupExtensionsTests
{
    [Fact]
    public void AddBybitExchange_Registers_Public_Provider_For_Public_Capability_Interfaces()
    {
        var services = CreateServices();

        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var derivativesDataProvider = scope.ServiceProvider.GetRequiredService<IDerivativesDataProvider>();
        var privateProviderFactory = scope.ServiceProvider.GetRequiredService<BybitPrivateAccountProviderFactory>();

        marketDataProvider.Should().NotBeNull();
        derivativesDataProvider.Should().NotBeNull();
        privateProviderFactory.Should().NotBeNull();
    }

    [Fact]
    public void AddBybitExchange_Uses_One_Scoped_Public_Provider_Instance_For_Public_Capability_Interfaces()
    {
        var services = CreateServices();

        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var derivativesDataProvider = scope.ServiceProvider.GetRequiredService<IDerivativesDataProvider>();

        marketDataProvider.Should().BeSameAs(derivativesDataProvider);
    }

    [Fact]
    public void AddBybitExchange_Creates_A_New_Public_Provider_For_Each_Scope()
    {
        var services = CreateServices();

        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var firstProvider = firstScope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var secondProvider = secondScope.ServiceProvider.GetRequiredService<IMarketDataProvider>();

        firstProvider.Should().NotBeSameAs(secondProvider);
    }

    [Fact]
    public void Private_Providers_Are_Created_Per_Credentials_Without_DI_Registration()
    {
        var services = CreateServices();
        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        var factory = serviceProvider.GetRequiredService<BybitPrivateAccountProviderFactory>();

        var firstProvider = factory.Create(new BybitCredentials("first-key", "first-secret"));
        var secondProvider = factory.Create(new BybitCredentials("second-key", "second-secret"));

        firstProvider.Should().BeAssignableTo<IPrivateAccountProvider>();
        secondProvider.Should().BeAssignableTo<IPrivateAccountProvider>();
        firstProvider.Should().NotBeSameAs(secondProvider);
        serviceProvider.GetService<IPrivateAccountProvider>().Should().BeNull();
    }

    [Fact]
    public void BybitClientFactory_Creates_Isolated_Public_And_Private_Clients()
    {
        var publicClient = BybitClientFactory.CreatePublicClient();
        var firstPrivateClient = BybitClientFactory.CreatePrivateClient(new BybitCredentials("first-key", "first-secret"));
        var secondPrivateClient = BybitClientFactory.CreatePrivateClient(new BybitCredentials("second-key", "second-secret"));

        publicClient.Should().NotBeSameAs(firstPrivateClient);
        firstPrivateClient.Should().NotBeSameAs(secondPrivateClient);
        GetApiCredentials(publicClient).Should().BeNull();
        GetApiCredentials(firstPrivateClient).Should().NotBeNull();
        GetApiCredentials(secondPrivateClient).Should().NotBeNull();
    }

    [Fact]
    public void ExchangeId_Contains_Bybit()
    {
        Enum.GetValues<ExchangeId>().Should().Contain(ExchangeId.Bybit);
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<IBybitRestClient>().Object);
        return services;
    }

    private static object? GetApiCredentials(IBybitRestClient client)
    {
        var clientOptions = client.V5Api.GetType()
            .GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .FirstOrDefault(property => property.Name == "ClientOptions")
            ?.GetValue(client.V5Api);
        return clientOptions?.GetType().GetProperty("ApiCredentials")?.GetValue(clientOptions);
    }
}
