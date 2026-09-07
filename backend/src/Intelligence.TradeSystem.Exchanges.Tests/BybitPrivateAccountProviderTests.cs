using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.Logging;
using Moq;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using DomainAccountType = Intelligence.TradeSystem.Domain.AccountType;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class BybitPrivateAccountProviderTests
{
    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Complete_With_Empty_Positions_On_Successful_Empty_Response()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, "BTCUSDT", null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition> { List = [] }));

        var provider = CreateProvider(trading);
        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear, "BTCUSDT");

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().BeEmpty();
        observation.Category.Should().Be(MarketCategory.Linear);
        observation.Symbol.Should().Be("BTCUSDT");
        observation.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Complete_With_Positions_On_Successful_Response()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List =
                [
                    new BybitPosition { Symbol = "BTCUSDT", Quantity = 1.5m, Side = PositionSide.Buy },
                ],
            }));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().ContainSingle(p => p.Symbol == "BTCUSDT" && p.Size == 1.5m);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Failed_On_Api_Error()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("boom"));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Failed);
        observation.Positions.Should().BeEmpty();
        observation.Error.Should().Be("boom");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Followed_By_Zero_Positions_Never_Looks_The_Same_As_Failed()
    {
        var emptyTrading = new Mock<IBybitRestClientApiTrading>();
        emptyTrading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition> { List = [] }));
        var emptyObservation = await CreateProvider(emptyTrading).GetOpenPositionsAsync(MarketCategory.Linear);

        var failedTrading = new Mock<IBybitRestClientApiTrading>();
        failedTrading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("boom"));
        var failedObservation = await CreateProvider(failedTrading).GetOpenPositionsAsync(MarketCategory.Linear);

        emptyObservation.Positions.Should().BeEmpty();
        failedObservation.Positions.Should().BeEmpty();
        emptyObservation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        failedObservation.Status.Should().Be(OpenPositionsObservationStatus.Failed);
        emptyObservation.Status.Should().NotBe(failedObservation.Status);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Follows_Pagination_Cursor_Before_Reporting_Complete()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "BTCUSDT", Quantity = 1m, Side = PositionSide.Buy }],
                NextPageCursor = "next-page",
            }));
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, "next-page", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "ETHUSDT", Quantity = 2m, Side = PositionSide.Sell }],
            }));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Complete);
        observation.Positions.Should().HaveCount(2);
        observation.Positions.Should().Contain(p => p.Symbol == "BTCUSDT");
        observation.Positions.Should().Contain(p => p.Symbol == "ETHUSDT");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Returns_Partial_When_A_Later_Page_Fails()
    {
        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitPosition>
            {
                List = [new BybitPosition { Symbol = "BTCUSDT", Quantity = 1m, Side = PositionSide.Buy }],
                NextPageCursor = "next-page",
            }));
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, "next-page", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateError<BybitResponse<BybitPosition>>("page 2 failed"));

        var provider = CreateProvider(trading);

        var observation = await provider.GetOpenPositionsAsync(MarketCategory.Linear);

        observation.Status.Should().Be(OpenPositionsObservationStatus.Partial);
        observation.Positions.Should().ContainSingle(p => p.Symbol == "BTCUSDT");
        observation.Error.Should().Be("page 2 failed");
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Throws_For_Spot_Category()
    {
        var provider = CreateProvider(new Mock<IBybitRestClientApiTrading>());

        var act = async () => await provider.GetOpenPositionsAsync(MarketCategory.Spot);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOpenPositionsAsync_Propagates_Cancellation_From_Bybit_Result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var trading = new Mock<IBybitRestClientApiTrading>();
        trading
            .Setup(t => t.GetPositionsAsync(
                Category.Linear, null, null, null, 200, null, cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitPosition>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(trading)
            .GetOpenPositionsAsync(MarketCategory.Linear, cancellationToken: cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Default_ExchangeFailureKind_Is_Unknown()
    {
        default(ExchangeFailureKind).Should().Be(ExchangeFailureKind.Unknown);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Returns_Complete_With_Mapped_Balance_On_Success()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance>
            {
                List =
                [
                    new BybitBalance
                    {
                        AccountType = BybitAccountType.Unified,
                        TotalEquity = 12_500m,
                        TotalWalletBalance = 12_000m,
                        TotalAvailableBalance = 8_000m,
                        TotalPerpUnrealizedPnl = 500m,
                        Assets =
                        [
                            new BybitAssetBalance
                            {
                                Asset = "USDT",
                                Equity = 12_500m,
                                UsdValue = 12_500m,
                                WalletBalance = 12_000m,
                                Free = 8_000m,
                            },
                        ],
                    },
                ],
            }));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Complete);
        observation.Failure.Should().BeNull();
        observation.Balance.Should().NotBeNull();
        observation.Balance!.AccountType.Should().Be(DomainAccountType.Unified);
        observation.Balance.TotalEquity.Should().Be(12_500m);
        observation.Balance.TotalAvailableBalance.Should().Be(8_000m);
        observation.Balance.Coins.Should().ContainSingle(coin =>
            coin.Coin == "USDT" && coin.WalletBalance == 12_000m && coin.AvailableBalance == 8_000m);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Returns_InvalidResponse_When_Success_Has_No_Balance()
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccess(new BybitResponse<BybitBalance> { List = [] }));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Failed);
        observation.Balance.Should().BeNull();
        observation.Failure.Should().BeEquivalentTo(
            new ExchangeFailure(ExchangeFailureKind.InvalidResponse, Retryable: false));
    }

    [Theory]
    [InlineData("10003", ErrorType.Unauthorized, ExchangeFailureKind.InvalidCredentials, false)]
    [InlineData("10005", ErrorType.Unauthorized, ExchangeFailureKind.PermissionDenied, false)]
    [InlineData("10006", ErrorType.RateLimitRequest, ExchangeFailureKind.RateLimited, true)]
    [InlineData("missing-credentials", ErrorType.MissingCredentials, ExchangeFailureKind.InvalidCredentials, false)]
    [InlineData("timeout", ErrorType.Timeout, ExchangeFailureKind.Timeout, true)]
    [InlineData("network", ErrorType.NetworkError, ExchangeFailureKind.Unavailable, true)]
    [InlineData("deserialization", ErrorType.DeserializationFailed, ExchangeFailureKind.InvalidResponse, false)]
    [InlineData("10016", ErrorType.SystemError, ExchangeFailureKind.Unavailable, true)]
    [InlineData("unknown-code", ErrorType.Unknown, ExchangeFailureKind.Unknown, false)]
    public async Task GetWalletBalanceAsync_Maps_Provider_Failure(
        string providerCode,
        ErrorType errorType,
        ExchangeFailureKind expectedKind,
        bool expectedRetryable)
    {
        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(providerCode, errorType));

        var observation = await CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified);

        observation.Status.Should().Be(AccountBalanceObservationStatus.Failed);
        observation.Balance.Should().BeNull();
        observation.Failure.Should().NotBeNull();
        observation.Failure!.Kind.Should().Be(expectedKind);
        observation.Failure.Retryable.Should().Be(expectedRetryable);
        observation.Failure.ProviderCode.Should().Be(providerCode);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_And_Passes_Token_To_Bybit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                It.Is<CancellationToken>(token => token == cancellation.Token)))
            .Returns(() => Task.FromCanceled<HttpResult<BybitResponse<BybitBalance>>>(cancellation.Token));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
        account.Verify(a => a.GetBalancesAsync(
            BybitAccountType.Unified,
            null,
            cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_From_Bybit_Result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task GetWalletBalanceAsync_Propagates_Cancellation_Without_Attaching_NonCancelled_Token()
    {
        using var cancellation = new CancellationTokenSource();

        var account = new Mock<IBybitRestClientApiAccount>();
        account
            .Setup(a => a.GetBalancesAsync(
                BybitAccountType.Unified,
                null,
                cancellation.Token))
            .ReturnsAsync(CreateProviderError<BybitResponse<BybitBalance>>(
                "cancelled",
                ErrorType.CancellationRequested));

        var act = () => CreateProvider(new Mock<IBybitRestClientApiTrading>(), account)
            .GetWalletBalanceAsync(DomainAccountType.Unified, cancellation.Token);

        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(default(CancellationToken));
        exception.Which.CancellationToken.IsCancellationRequested.Should().BeFalse();
    }

    private static BybitPrivateAccountProvider CreateProvider(
        Mock<IBybitRestClientApiTrading> trading,
        Mock<IBybitRestClientApiAccount>? account = null)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(a => a.Trading).Returns(trading.Object);
        if (account is not null)
        {
            v5Api.SetupGet(a => a.Account).Returns(account.Object);
        }

        var client = new Mock<IBybitRestClient>();
        client.SetupGet(c => c.V5Api).Returns(v5Api.Object);

        var loggerFactory = LoggerFactory.Create(_ => { });
        return new BybitPrivateAccountProvider(client.Object, loggerFactory.CreateLogger<BybitPrivateAccountProvider>());
    }

    private static HttpResult<T> CreateSuccess<T>(T data) => new("Bybit", data, null!);

    private static HttpResult<T> CreateError<T>(string message) =>
        new("Bybit", default!, new ServerError(ErrorType.Unknown, message, null!) { Message = message });

    private static HttpResult<T> CreateProviderError<T>(string providerCode, ErrorType errorType) =>
        new(
            "Bybit",
            default!,
            new ServerError(
                providerCode,
                new ErrorInfo(errorType, isTransient: false, "provider failure", [providerCode]),
                null!));
}
