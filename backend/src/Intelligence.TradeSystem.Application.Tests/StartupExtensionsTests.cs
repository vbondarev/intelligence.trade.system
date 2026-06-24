using FluentAssertions;
using Intelligence.TradeSystem.Abstractions;
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
}
