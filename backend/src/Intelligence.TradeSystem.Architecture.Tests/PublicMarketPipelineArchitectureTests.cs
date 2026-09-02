using System.Reflection;
using FluentAssertions;
using Xunit;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class PublicMarketPipelineArchitectureTests
{
    [Fact]
    public void MarketSnapshot_Does_Not_Contain_Portfolio_Properties()
    {
        var propertyTypes = typeof(MarketSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .ToArray();

        propertyTypes.Should().NotContain(typeof(PortfolioSnapshot));
        propertyTypes.Should().NotContain(typeof(OpenPositionSnapshot));
        typeof(MarketSnapshot).GetProperty("Portfolio").Should().BeNull();
    }

    [Fact]
    public void PublicMarketDataCollector_Does_Not_Depend_On_Private_Account_Provider()
    {
        var parameters = typeof(PublicMarketDataCollector).GetConstructors().Single().GetParameters();

        parameters.Select(p => p.ParameterType).Should().Equal(typeof(IMarketDataProvider), typeof(IDerivativesDataProvider));
        parameters.Should().NotContain(p => p.ParameterType == typeof(IPrivateAccountProvider));
    }

    [Fact]
    public void MarketAnalysisController_Uses_MarketSnapshot_Service_Abstraction()
    {
        var parameters = typeof(MarketAnalysisController).GetConstructors().Single().GetParameters();

        parameters.Should().Contain(p => p.ParameterType == typeof(IMarketSnapshotService));
        parameters.Should().NotContain(p => p.ParameterType == typeof(IPrivateAccountProvider));
    }

    [Fact]
    public void Application_And_Domain_Do_Not_Depend_On_BybitNet()
    {
        typeof(PublicMarketDataCollector).Assembly.GetReferencedAssemblies()
            .Should().NotContain(assembly => assembly.Name == "Bybit.Net");
        typeof(MarketSnapshot).Assembly.GetReferencedAssemblies()
            .Should().NotContain(assembly => assembly.Name == "Bybit.Net");
    }

    [Fact]
    public void Legacy_Bybit_Provider_Types_Are_Absent()
    {
        var exchangeAssembly = typeof(Intelligence.TradeSystem.Exchanges.StartupExtensions).Assembly;

        var exchangeNamespace = string.Join('.', "Intelligence", "TradeSystem", "Exchanges", "Bybit");
        var legacyNamespace = string.Join('.', "Intelligence", "TradeSystem", "Abstractions");

        exchangeAssembly.GetType($"{exchangeNamespace}.Bybit" + "Provider").Should().BeNull();
        exchangeAssembly.GetType($"{legacyNamespace}.IBybit" + "Provider").Should().BeNull();
    }
}
