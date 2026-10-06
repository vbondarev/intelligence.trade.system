using FluentAssertions;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Application.Accounts;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class ExchangeAccountControllerArchitectureTests
{
    [Fact]
    public void Exchange_accounts_controller_does_not_own_portfolio_reads()
    {
        var parameters = typeof(ExchangeAccountsController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(IExchangeAccountService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IExchangeAccountSyncService));
        parameters.Should().NotContain(typeof(PortfolioReadService));
        parameters.Should().NotContain(typeof(IPortfolioReadStore));
    }

    [Fact]
    public void Exchange_account_sync_controller_owns_only_manual_sync()
    {
        var parameters = typeof(ExchangeAccountSyncController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(IExchangeAccountSyncService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IExchangeAccountService));
        parameters.Should().NotContain(typeof(PortfolioReadService));
        parameters.Should().NotContain(typeof(IPortfolioReadStore));
    }

    [Fact]
    public void Exchange_account_portfolio_controller_uses_application_read_boundary()
    {
        var parameters = typeof(ExchangeAccountPortfolioController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(PortfolioReadService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IPortfolioReadStore));
    }
}
