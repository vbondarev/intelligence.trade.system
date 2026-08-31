using FluentAssertions;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Application.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests;

public sealed class StartupExtensionsTests
{
    [Fact]
    public void AddApplication_Registers_MarketDataCollector_And_MarketAnalysisService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IMarketDataProvider>().Object);
        services.AddSingleton(new Mock<IDerivativesDataProvider>().Object);
        services.AddSingleton(new Mock<IPrivateAccountProvider>().Object);

        services.AddApplication();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var collector = scope.ServiceProvider.GetRequiredService<IMarketDataCollector>();
        var analysisService = scope.ServiceProvider.GetRequiredService<IMarketAnalysisService>();

        collector.Should().BeOfType<MarketDataCollector>();
        analysisService.Should().BeOfType<MarketAnalysisService>();
    }

    [Fact]
    public void AddApplication_Registers_AiContextFormatter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IMarketDataProvider>().Object);
        services.AddSingleton(new Mock<IDerivativesDataProvider>().Object);
        services.AddSingleton(new Mock<IPrivateAccountProvider>().Object);

        services.AddApplication();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var formatter = scope.ServiceProvider.GetRequiredService<IAiContextFormatter>();

        formatter.Should().BeOfType<SnapshotTextFormatter>();
    }

    [Fact]
    public void AddApplication_Uses_One_Scoped_AiContextFormatter_Instance_Per_Scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IMarketDataProvider>().Object);
        services.AddSingleton(new Mock<IDerivativesDataProvider>().Object);
        services.AddSingleton(new Mock<IPrivateAccountProvider>().Object);

        services.AddApplication();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        var firstA = firstScope.ServiceProvider.GetRequiredService<IAiContextFormatter>();
        var firstB = firstScope.ServiceProvider.GetRequiredService<IAiContextFormatter>();
        var second = secondScope.ServiceProvider.GetRequiredService<IAiContextFormatter>();

        firstA.Should().BeSameAs(firstB);
        firstA.Should().NotBeSameAs(second);
    }
}
