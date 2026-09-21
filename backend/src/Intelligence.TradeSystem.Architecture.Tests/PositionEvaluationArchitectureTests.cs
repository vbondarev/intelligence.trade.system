using FluentAssertions;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Users;
using Xunit;

namespace Intelligence.TradeSystem.Architecture.Tests;

public sealed class PositionEvaluationArchitectureTests
{
    [Fact]
    public void Position_evaluation_controller_uses_application_boundary_only()
    {
        var parameters = typeof(PositionEvaluationController)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(PositionEvaluationService));
        parameters.Should().Contain(typeof(ICurrentUserContext));
        parameters.Should().NotContain(typeof(IMarketSnapshotService));
        parameters.Should().NotContain(typeof(IPrivateAccountProvider));
        parameters.Should().NotContain(typeof(IExchangeAccountSyncService));
    }

    [Fact]
    public void Position_evaluation_service_does_not_depend_on_private_sync_or_credentials()
    {
        var parameters = typeof(PositionEvaluationService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        parameters.Should().Contain(typeof(IPositionRepository));
        parameters.Should().Contain(typeof(IMarketSnapshotService));
        parameters.Should().NotContain(typeof(IPrivateAccountProvider));
        parameters.Should().NotContain(typeof(IExchangeAccountSyncService));
        parameters.Should().NotContain(typeof(IExchangeAccountSyncTransaction));
        parameters.Should().NotContain(typeof(IExchangeAccountCredentialStore));
    }
}
