using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.Logging;
using Moq;

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

    private static BybitPrivateAccountProvider CreateProvider(Mock<IBybitRestClientApiTrading> trading)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(a => a.Trading).Returns(trading.Object);

        var client = new Mock<IBybitRestClient>();
        client.SetupGet(c => c.V5Api).Returns(v5Api.Object);

        var loggerFactory = LoggerFactory.Create(_ => { });
        return new BybitPrivateAccountProvider(client.Object, loggerFactory.CreateLogger<BybitPrivateAccountProvider>());
    }

    private static HttpResult<T> CreateSuccess<T>(T data) => new("Bybit", data, null!);

    private static HttpResult<T> CreateError<T>(string message) =>
        new("Bybit", default!, new ServerError(ErrorType.Unknown, message, null!) { Message = message });
}
