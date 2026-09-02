using Intelligence.TradeSystem.Application.AI;
using Intelligence.TradeSystem.Application.Market;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests;

public sealed class StartupExtensionsTests
{
    [Fact]
    public void AddApplication_Registers_PublicMarketDataCollector_And_MarketSnapshotService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IMarketDataProvider>().Object);
        services.AddSingleton(new Mock<IDerivativesDataProvider>().Object);

        services.AddApplication();

        using var serviceProvider = services.BuildServiceProvider(validateScopes: true);
        using var scope = serviceProvider.CreateScope();

        var collector = scope.ServiceProvider.GetRequiredService<IPublicMarketDataCollector>();
        var analysisService = scope.ServiceProvider.GetRequiredService<IMarketSnapshotService>();

        collector.Should().BeOfType<PublicMarketDataCollector>();
        analysisService.Should().BeOfType<MarketSnapshotService>();
    }

    [Fact]
    public void AddApplication_Registers_AiContextFormatter()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Mock<IMarketDataProvider>().Object);
        services.AddSingleton(new Mock<IDerivativesDataProvider>().Object);

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
