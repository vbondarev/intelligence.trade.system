using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Controllers;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class ExchangeAccountsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ExchangeAccountsControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ConnectBybit_Requires_Authentication()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/exchange-accounts/bybit",
            new
            {
                apiKey = "api-key",
                apiSecret = "api-secret",
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConnectBybit_Returns_Safe_Connected_Account_Response()
    {
        var account = CreateAccount();
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service
            .Setup(value => value.ConnectAsync(
                ExchangeId.Bybit,
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountConnectionResult.Connected(account));
        var controller = CreateController(service);

        var action = await controller.ConnectBybit(
            new ConnectExchangeAccountRequest
            {
                ApiKey = "api-key",
                ApiSecret = "api-secret",
            },
            CancellationToken.None);

        var response = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        response.Value.Should().BeOfType<ExchangeAccountResponse>();
        var json = JsonSerializer.Serialize(response.Value);
        json.Should().NotContain("apiKey");
        json.Should().NotContain("apiSecret");
        json.Should().NotContain("api-secret");
        service.VerifyAll();
    }

    [Theory]
    [InlineData(ExchangeAccountConnectionOutcome.InvalidCredentials, 400)]
    [InlineData(ExchangeAccountConnectionOutcome.PermissionsRejected, 403)]
    [InlineData(ExchangeAccountConnectionOutcome.Unavailable, 503)]
    public async Task ConnectBybit_Maps_Verification_Outcomes_To_Client_Errors(
        ExchangeAccountConnectionOutcome outcome,
        int expectedStatus)
    {
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service
            .Setup(value => value.ConnectAsync(
                ExchangeId.Bybit,
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExchangeAccountConnectionResult.Failed(outcome));
        var controller = CreateController(service);

        var action = await controller.ConnectBybit(
            new ConnectExchangeAccountRequest
            {
                ApiKey = "api-key",
                ApiSecret = "api-secret",
            },
            CancellationToken.None);

        var result = action.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(expectedStatus);
        result.Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact]
    public async Task ConnectBybit_Validates_Required_Fields_Without_Calling_Application()
    {
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        var controller = CreateController(service);

        var action = await controller.ConnectBybit(
            new ConnectExchangeAccountRequest
            {
                ApiKey = " ",
                ApiSecret = "api-secret",
            },
            CancellationToken.None);

        action.Result.Should().BeOfType<BadRequestObjectResult>();
        service.Verify(
            value => value.ConnectAsync(
                It.IsAny<ExchangeId>(),
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConnectBybit_Trims_ApiKey_And_ApiSecret_Before_Application_Call()
    {
        ExchangeAccountCredentialSecret? capturedCredentials = null;
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service
            .Setup(value => value.ConnectAsync(
                ExchangeId.Bybit,
                It.IsAny<ExchangeAccountCredentialSecret>(),
                It.IsAny<CancellationToken>()))
            .Callback<ExchangeId, ExchangeAccountCredentialSecret, CancellationToken>(
                (_, credentials, _) => capturedCredentials = credentials)
            .ReturnsAsync(ExchangeAccountConnectionResult.Connected(CreateAccount()));
        var controller = CreateController(service);

        await controller.ConnectBybit(
            new ConnectExchangeAccountRequest
            {
                ApiKey = " api-key ",
                ApiSecret = " api-secret ",
            },
            CancellationToken.None);

        capturedCredentials.Should().NotBeNull();
        capturedCredentials!.Use((apiKey, apiSecret) =>
        {
            apiKey.Should().Be("api-key");
            apiSecret.Should().Be("api-secret");
        });
        service.VerifyAll();
    }

    [Fact]
    public async Task Disconnect_Returns_NotFound_For_Foreign_Or_Missing_Account()
    {
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service
            .Setup(value => value.DisconnectAsync(
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExchangeAccount?)null);
        var controller = CreateController(service);

        var action = await controller.Disconnect(Guid.NewGuid(), CancellationToken.None);

        action.Result.Should().BeOfType<NotFoundResult>();
        service.VerifyAll();
    }

    [Fact]
    public async Task Disconnect_Returns_Disabled_Account_Without_Credentials()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Disabled,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        var service = new Mock<IExchangeAccountService>(MockBehavior.Strict);
        service
            .Setup(value => value.DisconnectAsync(account.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        var controller = CreateController(service);

        var action = await controller.Disconnect(account.Id.Value, CancellationToken.None);

        var response = action.Result.Should().BeOfType<OkObjectResult>().Subject;
        response.Value.Should().BeOfType<ExchangeAccountResponse>()
            .Which.ConnectionStatus.Should().Be(ExchangeAccountConnectionStatus.Disabled);
        service.VerifyAll();
    }

    private static ExchangeAccountsController CreateController(
        Mock<IExchangeAccountService> service)
    {
        var controller = new ExchangeAccountsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };
        return controller;
    }

    private static ExchangeAccount CreateAccount() =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
}
