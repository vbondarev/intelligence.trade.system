using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Public;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BybitCategory = global::Bybit.Net.Enums.Category;
using DomainKlineInterval = Intelligence.TradeSystem.Domain.KlineInterval;
using BybitKlineInterval = global::Bybit.Net.Enums.KlineInterval;
using DomainOpenInterestInterval = Intelligence.TradeSystem.Domain.OpenInterestInterval;
using BybitOpenInterestInterval = global::Bybit.Net.Enums.OpenInterestInterval;
using DomainLongShortRatioPeriod = Intelligence.TradeSystem.Domain.LongShortRatioPeriod;
using BybitDataPeriod = global::Bybit.Net.Enums.DataPeriod;
using BybitOrderSide = global::Bybit.Net.Enums.OrderSide;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class BybitPublicMarketProviderTests
{
    [Fact]
    public async Task GetKlinesAsync_passes_arguments_and_maps_response()
    {
        var exchangeData = CreateExchangeData();
        var cancellation = new CancellationTokenSource().Token;
        var start = new DateTime(2026, 1, 1);
        var end = start.AddHours(1);
        exchangeData
            .Setup(data => data.GetKlinesAsync(
                BybitCategory.Linear,
                "BTCUSDT",
                BybitKlineInterval.FifteenMinutes,
                start,
                end,
                10,
                cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitKline>
            {
                List =
                [
                    new BybitKline
                    {
                        StartTime = start,
                        OpenPrice = 100m,
                        HighPrice = 110m,
                        LowPrice = 90m,
                        ClosePrice = 105m,
                        Volume = 12m,
                        QuoteVolume = 1200m,
                    },
                ],
            }));

        var result = await CreateProvider(exchangeData.Object)
            .GetKlinesAsync("BTCUSDT", MarketCategory.Linear, DomainKlineInterval.FifteenMinutes, start, end, 10, cancellation);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new Kline("BTCUSDT", MarketCategory.Linear, DomainKlineInterval.FifteenMinutes, start, 100m, 110m, 90m, 105m, 12m, 1200m));
        exchangeData.VerifyAll();
    }

    [Fact]
    public async Task GetKlinesAsync_returns_empty_on_empty_success()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetKlinesAsync(
                It.IsAny<BybitCategory>(),
                It.IsAny<string>(),
                It.IsAny<BybitKlineInterval>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(new BybitResponse<BybitKline> { List = [] }));

        var result = await CreateProvider(exchangeData.Object)
            .GetKlinesAsync("BTCUSDT", MarketCategory.Linear, DomainKlineInterval.OneHour);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetKlinesAsync_returns_empty_on_provider_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetKlinesAsync(
                It.IsAny<BybitCategory>(),
                It.IsAny<string>(),
                It.IsAny<BybitKlineInterval>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitKline>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetKlinesAsync("BTCUSDT", MarketCategory.Linear, DomainKlineInterval.OneHour);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTickerAsync_uses_spot_endpoint_and_maps_nullable_quotes()
    {
        var exchangeData = CreateExchangeData();
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetSpotTickersAsync("BTCUSDT", cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitSpotTicker>
            {
                List = [new BybitSpotTicker { LastPrice = 100m, BestBidPrice = null, BestAskQuantity = null }],
            }));

        var ticker = await CreateProvider(exchangeData.Object)
            .GetTickerAsync("BTCUSDT", MarketCategory.Spot, cancellation);

        ticker.Should().NotBeNull();
        ticker!.Category.Should().Be(MarketCategory.Spot);
        ticker.LastPrice.Should().Be(100m);
        ticker.BidPrice.Should().Be(0m);
        ticker.AskSize.Should().Be(0m);
        exchangeData.Verify(data => data.GetLinearInverseTickersAsync(
            It.IsAny<BybitCategory>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        exchangeData.Verify(data => data.GetSpotTickersAsync("BTCUSDT", cancellation), Times.Once);
    }

    [Fact]
    public async Task GetTickerAsync_spot_returns_null_on_provider_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetSpotTickersAsync("BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitSpotTicker>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetTickerAsync("BTCUSDT", MarketCategory.Spot);

        result.Should().BeNull();
        exchangeData.Verify(data => data.GetSpotTickersAsync("BTCUSDT", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTickerAsync_spot_returns_null_on_empty_success()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetSpotTickersAsync("BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(new BybitResponse<BybitSpotTicker> { List = [] }));

        var result = await CreateProvider(exchangeData.Object)
            .GetTickerAsync("BTCUSDT", MarketCategory.Spot);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(MarketCategory.Linear, BybitCategory.Linear)]
    [InlineData(MarketCategory.Inverse, BybitCategory.Inverse)]
    public async Task GetTickerAsync_uses_derivatives_endpoint(MarketCategory category, BybitCategory bybitCategory)
    {
        var exchangeData = CreateExchangeData();
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetLinearInverseTickersAsync(
                bybitCategory, "BTCUSDT", null, null, cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitLinearInverseTicker>
            {
                List = [new BybitLinearInverseTicker { LastPrice = 100m, MarkPrice = 101m, IndexPrice = 99m }],
            }));

        var ticker = await CreateProvider(exchangeData.Object).GetTickerAsync("BTCUSDT", category, cancellation);

        ticker.Should().NotBeNull();
        ticker!.Category.Should().Be(category);
        ticker.MarkPrice.Should().Be(101m);
        exchangeData.Verify(data => data.GetSpotTickersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        exchangeData.Verify(data => data.GetLinearInverseTickersAsync(
            bybitCategory, "BTCUSDT", null, null, cancellation), Times.Once);
    }

    [Fact]
    public async Task GetTickerAsync_derivatives_returns_null_on_provider_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseTickersAsync(
                BybitCategory.Linear, "BTCUSDT", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitLinearInverseTicker>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetTickerAsync("BTCUSDT", MarketCategory.Linear);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTickerAsync_derivatives_returns_null_on_empty_success()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetLinearInverseTickersAsync(
                BybitCategory.Inverse, "BTCUSD", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(new BybitResponse<BybitLinearInverseTicker> { List = [] }));

        var result = await CreateProvider(exchangeData.Object)
            .GetTickerAsync("BTCUSD", MarketCategory.Inverse);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderBookAsync_returns_null_on_failure_and_rejects_no_categories()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetOrderbookAsync(
                BybitCategory.Linear, "BTCUSDT", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitOrderbook>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetOrderBookAsync("BTCUSDT", MarketCategory.Linear, 25);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrderBookAsync_maps_successful_book()
    {
        var exchangeData = CreateExchangeData();
        var timestamp = DateTime.UtcNow;
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetOrderbookAsync(
                BybitCategory.Linear, "BTCUSDT", 25, cancellation))
            .ReturnsAsync(Success(new BybitOrderbook
            {
                Symbol = "BTCUSDT",
                Timestamp = timestamp,
                Bids = [new BybitOrderbookEntry { Price = 100m, Quantity = 2m }],
                Asks = [new BybitOrderbookEntry { Price = 101m, Quantity = 3m }],
            }));

        var result = await CreateProvider(exchangeData.Object)
            .GetOrderBookAsync("BTCUSDT", MarketCategory.Linear, 25, cancellation);

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be(MarketCategory.Linear);
        result.CapturedAt.Should().Be(new DateTimeOffset(timestamp));
        result.Bids.Should().ContainSingle().Which.Should().BeEquivalentTo(new OrderBookEntry(100m, 2m));
        result.Asks.Should().ContainSingle().Which.Should().BeEquivalentTo(new OrderBookEntry(101m, 3m));
        exchangeData.Verify(data => data.GetOrderbookAsync(
            BybitCategory.Linear, "BTCUSDT", 25, cancellation), Times.Once);
    }

    [Fact]
    public async Task GetRecentTradesAsync_returns_empty_on_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetTradeHistoryAsync(
                BybitCategory.Spot, "BTCUSDT", null, null, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitTradeHistory>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetRecentTradesAsync("BTCUSDT", MarketCategory.Spot, 5);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTradesAsync_maps_successful_trades()
    {
        var exchangeData = CreateExchangeData();
        var timestamp = DateTime.UtcNow;
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetTradeHistoryAsync(
                BybitCategory.Inverse, "BTCUSD", null, null, 5, cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitTradeHistory>
            {
                List =
                [
                    new BybitTradeHistory
                    {
                        Timestamp = timestamp,
                        Side = BybitOrderSide.Sell,
                        Quantity = 2m,
                        Price = 101m,
                    },
                ],
            }));

        var result = await CreateProvider(exchangeData.Object)
            .GetRecentTradesAsync("BTCUSD", MarketCategory.Inverse, 5, cancellation);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new Trade("BTCUSD", MarketCategory.Inverse, timestamp, TradeSide.Sell, 2m, 101m));
        exchangeData.Verify(data => data.GetTradeHistoryAsync(
            BybitCategory.Inverse, "BTCUSD", null, null, 5, cancellation), Times.Once);
    }
    [Fact]
    public async Task GetOpenInterestHistoryAsync_returns_empty_on_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetOpenInterestAsync(
                BybitCategory.Linear,
                "BTCUSDT",
                BybitOpenInterestInterval.OneHour,
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                3,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitOpenInterest>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetOpenInterestHistoryAsync("BTCUSDT", MarketCategory.Linear, DomainOpenInterestInterval.OneHour, limit: 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOpenInterestHistoryAsync_maps_successful_entries()
    {
        var exchangeData = CreateExchangeData();
        var start = new DateTime(2026, 1, 1);
        var end = start.AddHours(1);
        var timestamp = start.AddMinutes(15);
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetOpenInterestAsync(
                BybitCategory.Linear, "BTCUSDT", BybitOpenInterestInterval.OneHour,
                start, end, 3, null, cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitOpenInterest>
            {
                List = [new BybitOpenInterest { Timestamp = timestamp, OpenInterest = 123m }],
            }));

        var result = await CreateProvider(exchangeData.Object)
            .GetOpenInterestHistoryAsync(
                "BTCUSDT", MarketCategory.Linear, DomainOpenInterestInterval.OneHour, start, end, 3, cancellation);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new OpenInterestEntry("BTCUSDT", MarketCategory.Linear, timestamp, 123m));
        exchangeData.Verify(data => data.GetOpenInterestAsync(
            BybitCategory.Linear, "BTCUSDT", BybitOpenInterestInterval.OneHour,
            start, end, 3, null, cancellation), Times.Once);
    }
    [Fact]
    public async Task GetFundingRateHistoryAsync_returns_empty_on_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetFundingRateHistoryAsync(
                BybitCategory.Inverse, "BTCUSD", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitResponse<BybitFundingHistory>>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetFundingRateHistoryAsync("BTCUSD", MarketCategory.Inverse, limit: 3);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFundingRateHistoryAsync_maps_successful_entries()
    {
        var exchangeData = CreateExchangeData();
        var start = new DateTime(2026, 1, 1);
        var end = start.AddHours(1);
        var timestamp = start.AddMinutes(15);
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetFundingRateHistoryAsync(
                BybitCategory.Inverse, "BTCUSD", start, end, 3, cancellation))
            .ReturnsAsync(Success(new BybitResponse<BybitFundingHistory>
            {
                List = [new BybitFundingHistory { Timestamp = timestamp, FundingRate = 0.001m }],
            }));

        var result = await CreateProvider(exchangeData.Object)
            .GetFundingRateHistoryAsync("BTCUSD", MarketCategory.Inverse, start, end, 3, cancellation);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new FundingRateEntry("BTCUSD", MarketCategory.Inverse, timestamp, 0.001m));
        exchangeData.Verify(data => data.GetFundingRateHistoryAsync(
            BybitCategory.Inverse, "BTCUSD", start, end, 3, cancellation), Times.Once);
    }

    [Fact]
    public async Task GetLongShortRatioHistoryAsync_maps_successful_entries()
    {
        var exchangeData = CreateExchangeData();
        var start = new DateTime(2026, 1, 1);
        var end = start.AddHours(1);
        var timestamp = start.AddMinutes(15);
        var cancellation = new CancellationTokenSource().Token;
        exchangeData
            .Setup(data => data.GetLongShortRatioAsync(
                BybitCategory.Linear, "BTCUSDT", BybitDataPeriod.OneHour,
                start, end, 3, cancellation))
            .ReturnsAsync(Success<BybitLongShortRatio[]>(
                [new BybitLongShortRatio { Timestamp = timestamp, BuyRatio = 0.6m, SellRatio = 0.4m }]));

        var result = await CreateProvider(exchangeData.Object)
            .GetLongShortRatioHistoryAsync(
                "BTCUSDT", MarketCategory.Linear, DomainLongShortRatioPeriod.OneHour, start, end, 3, cancellation);

        result.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new LongShortRatioEntry("BTCUSDT", MarketCategory.Linear, timestamp, 0.6m, 0.4m));
        exchangeData.Verify(data => data.GetLongShortRatioAsync(
            BybitCategory.Linear, "BTCUSDT", BybitDataPeriod.OneHour,
            start, end, 3, cancellation), Times.Once);
    }

    [Fact]
    public async Task GetLongShortRatioHistoryAsync_returns_empty_on_provider_failure()
    {
        var exchangeData = CreateExchangeData();
        exchangeData
            .Setup(data => data.GetLongShortRatioAsync(
                BybitCategory.Linear, "BTCUSDT", BybitDataPeriod.OneHour,
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error<BybitLongShortRatio[]>("failed"));

        var result = await CreateProvider(exchangeData.Object)
            .GetLongShortRatioHistoryAsync(
                "BTCUSDT", MarketCategory.Linear, DomainLongShortRatioPeriod.OneHour, limit: 3);

        result.Should().BeEmpty();
    }
    [Fact]
    public async Task Derivatives_history_rejects_spot_before_transport_calls()
    {
        var exchangeData = CreateExchangeData();
        var provider = CreateProvider(exchangeData.Object);

        await FluentActions.Invoking(() => provider.GetOpenInterestHistoryAsync(
                "BTCUSDT", MarketCategory.Spot, DomainOpenInterestInterval.OneHour))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => provider.GetFundingRateHistoryAsync(
                "BTCUSDT", MarketCategory.Spot))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => provider.GetLongShortRatioHistoryAsync(
                "BTCUSDT", MarketCategory.Spot, DomainLongShortRatioPeriod.OneHour))
            .Should().ThrowAsync<ArgumentException>();

        exchangeData.Verify(data => data.GetOpenInterestAsync(
            It.IsAny<BybitCategory>(), It.IsAny<string>(), It.IsAny<BybitOpenInterestInterval>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        exchangeData.Verify(data => data.GetFundingRateHistoryAsync(
            It.IsAny<BybitCategory>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        exchangeData.Verify(data => data.GetLongShortRatioAsync(
            It.IsAny<BybitCategory>(), It.IsAny<string>(), It.IsAny<BybitDataPeriod>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IBybitRestClientApiExchangeData> CreateExchangeData() => new();

    private static BybitPublicMarketProvider CreateProvider(IBybitRestClientApiExchangeData exchangeData)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(api => api.ExchangeData).Returns(exchangeData);
        var client = new Mock<IBybitRestClient>();
        client.SetupGet(api => api.V5Api).Returns(v5Api.Object);
        return new BybitPublicMarketProvider(client.Object, NullLogger<BybitPublicMarketProvider>.Instance);
    }

    private static HttpResult<T> Success<T>(T data) => new("Bybit", data, null!);

    private static HttpResult<T> Error<T>(string message) =>
        new("Bybit", default!, new ServerError(ErrorType.Unknown, message, null!) { Message = message });
}
