using Bybit.Net.Interfaces.Clients;
using FluentAssertions;
using Intelligence.TradeSystem.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class StartupExtensionsTests
{
    [Fact]
    public void AddBybitExchange_Registers_BybitProvider_For_All_Exchange_Capability_Interfaces()
    {
        var services = CreateServices();

        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var derivativesDataProvider = scope.ServiceProvider.GetRequiredService<IDerivativesDataProvider>();
        var privateAccountProvider = scope.ServiceProvider.GetRequiredService<IPrivateAccountProvider>();
        var bybitProvider = scope.ServiceProvider.GetRequiredService<IBybitProvider>();

        marketDataProvider.Should().NotBeNull();
        derivativesDataProvider.Should().NotBeNull();
        privateAccountProvider.Should().NotBeNull();
        bybitProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddBybitExchange_Uses_One_Scoped_BybitProvider_Instance_For_All_Registered_Interfaces()
    {
        var services = CreateServices();

        services.AddBybitExchange();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
        var derivativesDataProvider = scope.ServiceProvider.GetRequiredService<IDerivativesDataProvider>();
        var privateAccountProvider = scope.ServiceProvider.GetRequiredService<IPrivateAccountProvider>();
        var bybitProvider = scope.ServiceProvider.GetRequiredService<IBybitProvider>();

        marketDataProvider.Should().BeSameAs(derivativesDataProvider);
        marketDataProvider.Should().BeSameAs(privateAccountProvider);
        marketDataProvider.Should().BeSameAs(bybitProvider);
    }

    [Fact]
    public void AddBybitExchange_Creates_A_New_BybitProvider_For_Each_Scope()
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
    public void IBybitProvider_Remains_Assignable_To_All_Neutral_Capability_Interfaces()
    {
        typeof(IMarketDataProvider).IsAssignableFrom(typeof(IBybitProvider)).Should().BeTrue();
        typeof(IDerivativesDataProvider).IsAssignableFrom(typeof(IBybitProvider)).Should().BeTrue();
        typeof(IPrivateAccountProvider).IsAssignableFrom(typeof(IBybitProvider)).Should().BeTrue();
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
}

