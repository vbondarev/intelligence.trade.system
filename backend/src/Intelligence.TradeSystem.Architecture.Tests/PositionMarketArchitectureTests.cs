using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Users;
using FluentAssertions;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class PositionMarketArchitectureTests
{
    [Fact]
    public void Position_market_controller_uses_application_boundary_only()
    {
        var parameters = typeof(PositionMarketController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(PositionMarketService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IMarketSnapshotService));
        parameters.Should().NotContain(typeof(IMarketDataProvider));
        parameters.Should().NotContain(typeof(IPrivateAccountProvider));
    }

    [Fact]
    public void Position_market_service_does_not_depend_on_private_account_capability()
    {
        var parameters = typeof(PositionMarketService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(IPositionMarketIdentityStore));
        parameters.Should().Contain(typeof(IMarketSnapshotService));
        parameters.Should().Contain(typeof(IMarketDataProvider));
        parameters.Should().NotContain(typeof(IPrivateAccountProvider));
    }
}
