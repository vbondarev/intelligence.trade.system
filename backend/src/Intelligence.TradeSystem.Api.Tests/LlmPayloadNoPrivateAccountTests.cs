using System.Reflection;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class LlmPayloadNoPrivateAccountTests
{
    [Fact]
    public void PublicMarketDataCollector_Constructor_Does_Not_Require_Private_Account_Provider()
    {
        var parameters = typeof(PublicMarketDataCollector)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();

        parameters.Select(p => p.ParameterType).Should().Equal(
            typeof(IMarketDataProvider),
            typeof(IDerivativesDataProvider));
        parameters.Should().NotContain(p => p.ParameterType == typeof(IPrivateAccountProvider));
    }

    [Fact]
    public void MarketAnalysisController_Uses_MarketSnapshotService_Abstraction()
    {
        var parameters = typeof(MarketAnalysisController)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();

        parameters.Should().Contain(p => p.ParameterType == typeof(IMarketSnapshotService));
        parameters.Should().NotContain(p => p.ParameterType == typeof(IPrivateAccountProvider));
    }
}
