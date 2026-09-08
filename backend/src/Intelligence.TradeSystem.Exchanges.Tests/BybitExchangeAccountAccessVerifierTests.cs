using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.Logging;
using Moq;
using BybitAccountType = Bybit.Net.Enums.AccountType;

namespace Intelligence.TradeSystem.Exchanges.Tests;

[Collection("BybitExchangeTelemetry")]
public sealed class BybitExchangeAccountAccessVerifierTests
{
    [Fact]
    public async Task VerifyAsync_Confirms_ReadOnly_Metadata_And_Read_Capabilities()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(api => api.GetPositionsAsync(
                Category.Linear,
                null,
                null,
                null,
                200,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition> { List = [] }));

        var account = CreateAccountApi(readOnly: true);
        account
            .Setup(api => api.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance>
            {
                List = [new BybitBalance { AccountType = BybitAccountType.Unified }],
            }));

        var client = CreateClient(account, trading);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var factory = new BybitPrivateAccountProviderFactory(
            loggerFactory,
            _ => client.Object);
        var verifier = new BybitExchangeAccountAccessVerifier(factory);

        var result = await verifier.VerifyAsync(
            ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret("api-key", "api-secret"));

        result.Status.Should().Be(ExchangeAccountAccessVerificationStatus.Verified);
        result.Capabilities.Should().Be(
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        client.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_Rejects_Write_Enabled_Key_Before_Read_Operations()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        var account = CreateAccountApi(readOnly: false);
        var client = CreateClient(account, trading);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var factory = new BybitPrivateAccountProviderFactory(
            loggerFactory,
            _ => client.Object);
        var verifier = new BybitExchangeAccountAccessVerifier(factory);

        var result = await verifier.VerifyAsync(
            ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret("api-key", "api-secret"));

        result.Status.Should().Be(ExchangeAccountAccessVerificationStatus.PermissionsRejected);
        result.Capabilities.Should().Be(ExchangeAccountCapabilities.None);
        account.Verify(
            api => api.GetBalancesAsync(
                It.IsAny<BybitAccountType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        trading.Verify(
            api => api.GetPositionsAsync(
                It.IsAny<Category>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_Rejects_Key_Without_Required_Metadata_Permissions()
    {
        var account = CreateAccountApi(readOnly: true, hasPermissions: false);
        var client = CreateClient(account, new Mock<IBybitRestClientApiTrading>());
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var verifier = new BybitExchangeAccountAccessVerifier(
            new BybitPrivateAccountProviderFactory(loggerFactory, _ => client.Object));

        var result = await verifier.VerifyAsync(
            ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret("api-key", "api-secret"));

        result.Status.Should().Be(ExchangeAccountAccessVerificationStatus.PermissionsRejected);
    }

    [Fact]
    public async Task VerifyAsync_Distinguishes_Temporary_Bybit_Failure_From_Invalid_Credentials()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(api => api.GetApiKeyInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitApiKeyInfo>("timeout", ErrorType.Timeout));
        var client = CreateClient(account, new Mock<IBybitRestClientApiTrading>());
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var verifier = new BybitExchangeAccountAccessVerifier(
            new BybitPrivateAccountProviderFactory(loggerFactory, _ => client.Object));

        var result = await verifier.VerifyAsync(
            ExchangeId.Bybit,
            new ExchangeAccountCredentialSecret("api-key", "api-secret"));

        result.Status.Should().Be(ExchangeAccountAccessVerificationStatus.Unavailable);
    }

    private static Mock<IBybitRestClientApiAccount> CreateAccountApi(
        bool readOnly,
        bool hasPermissions = true)
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(api => api.GetApiKeyInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitApiKeyInfo
            {
                Readonly = readOnly,
                Permissions = hasPermissions
                    ? new BybitPermissions
                    {
                        Wallet = ["AccountTransfer"],
                        ContractTrade = ["Position"],
                    }
                    : new BybitPermissions
                    {
                        Wallet = [],
                        ContractTrade = [],
                    },
            }));
        return account;
    }

    private static Mock<IBybitRestClient> CreateClient(
        Mock<IBybitRestClientApiAccount> account,
        Mock<IBybitRestClientApiTrading> trading)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(api => api.Account).Returns(account.Object);
        v5Api.SetupGet(api => api.Trading).Returns(trading.Object);

        var client = new Mock<IBybitRestClient>();
        client.SetupGet(value => value.V5Api).Returns(v5Api.Object);
        return client;
    }

    private static HttpResult<T> CreateSuccess<T>(T data) => new("Bybit", data, null!);

    private static HttpResult<T> CreateError<T>(string code, ErrorType errorType) =>
        new(
            "Bybit",
            default!,
            new ServerError(
                code,
                new ErrorInfo(errorType, isTransient: false, "provider failure", [code]),
                null!));
}
