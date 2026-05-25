using FluentAssertions;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests;

public sealed class MarketDataCollectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CollectAsync_Throws_When_Symbol_Is_Null_Or_Whitespace(string? symbol)
    {
        var collector = CreateCollector();

        var act = () => collector.CollectAsync(ExchangeId.Bybit, symbol!, MarketCategory.Linear);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CollectAsync_Throws_When_Exchange_Is_Not_Supported()
    {
        var collector = CreateCollector();

        var act = () => collector.CollectAsync((ExchangeId)999, "BTCUSDT", MarketCategory.Linear);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task CollectAsync_Collects_All_Expected_Data_For_Derivatives_Market()
    {
        var marketDataProvider = new Mock<IMarketDataProvider>();
        var derivativesDataProvider = new Mock<IDerivativesDataProvider>();
        var privateAccountProvider = new Mock<IPrivateAccountProvider>();

        var ticker = CreateTicker();
        var orderBook = CreateOrderBook();
        var trades = new[] { CreateTrade() };
        var m15Klines = CreateKlines(KlineInterval.FifteenMinutes, 20);
        var h1Klines = CreateKlines(KlineInterval.OneHour, 20);
        var h4Klines = CreateKlines(KlineInterval.FourHours, 20);
        var d1Klines = CreateKlines(KlineInterval.OneDay, 20);
        var openInterestEntries = new[] { CreateOpenInterestEntry() };
        var fundingRateEntries = new[] { CreateFundingRateEntry() };
        var longShortRatioEntries = new[] { CreateLongShortRatioEntry() };
        var balance = CreateBalance();
        var positions = new[] { CreatePosition() };

        marketDataProvider.Setup(x => x.GetTickerAsync("BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticker);
        marketDataProvider.Setup(x => x.GetOrderBookAsync("BTCUSDT", MarketCategory.Linear, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(orderBook);
        marketDataProvider.Setup(x => x.GetRecentTradesAsync("BTCUSDT", MarketCategory.Linear, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(trades);
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.FifteenMinutes, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(m15Klines);
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.OneHour, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(h1Klines);
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.FourHours, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(h4Klines);
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.OneDay, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(d1Klines);

        derivativesDataProvider.Setup(x => x.GetOpenInterestHistoryAsync("BTCUSDT", MarketCategory.Linear, OpenInterestInterval.FiveMinutes, null, null, 48, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openInterestEntries);
        derivativesDataProvider.Setup(x => x.GetFundingRateHistoryAsync("BTCUSDT", MarketCategory.Linear, null, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fundingRateEntries);
        derivativesDataProvider.Setup(x => x.GetLongShortRatioHistoryAsync("BTCUSDT", MarketCategory.Linear, LongShortRatioPeriod.FiveMinutes, null, null, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(longShortRatioEntries);

        privateAccountProvider.Setup(x => x.GetWalletBalanceAsync(AccountType.Unified, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        privateAccountProvider.Setup(x => x.GetOpenPositionsAsync(MarketCategory.Linear, "BTCUSDT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var collector = new MarketDataCollector(marketDataProvider.Object, derivativesDataProvider.Object, privateAccountProvider.Object);

        var result = await collector.CollectAsync(ExchangeId.Bybit, " BTCUSDT ", MarketCategory.Linear);

        result.ExchangeId.Should().Be(ExchangeId.Bybit);
        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be(MarketCategory.Linear);
        result.Ticker.Should().BeSameAs(ticker);
        result.OrderBook.Should().BeSameAs(orderBook);
        result.Trades.Should().BeEquivalentTo(trades);
        result.M15Klines.Should().BeEquivalentTo(m15Klines);
        result.H1Klines.Should().BeEquivalentTo(h1Klines);
        result.H4Klines.Should().BeEquivalentTo(h4Klines);
        result.D1Klines.Should().BeEquivalentTo(d1Klines);
        result.OpenInterestEntries.Should().BeEquivalentTo(openInterestEntries);
        result.FundingRateEntries.Should().BeEquivalentTo(fundingRateEntries);
        result.LongShortRatioEntries.Should().BeEquivalentTo(longShortRatioEntries);
        result.WalletBalance.Should().BeSameAs(balance);
        result.OpenPositions.Should().BeEquivalentTo(positions);

        marketDataProvider.Verify(x => x.GetTickerAsync("BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetOrderBookAsync("BTCUSDT", MarketCategory.Linear, 50, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetRecentTradesAsync("BTCUSDT", MarketCategory.Linear, 60, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.FifteenMinutes, null, null, 250, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.OneHour, null, null, 250, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.FourHours, null, null, 250, It.IsAny<CancellationToken>()), Times.Once);
        marketDataProvider.Verify(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Linear, KlineInterval.OneDay, null, null, 250, It.IsAny<CancellationToken>()), Times.Once);
        derivativesDataProvider.Verify(x => x.GetOpenInterestHistoryAsync("BTCUSDT", MarketCategory.Linear, OpenInterestInterval.FiveMinutes, null, null, 48, It.IsAny<CancellationToken>()), Times.Once);
        derivativesDataProvider.Verify(x => x.GetFundingRateHistoryAsync("BTCUSDT", MarketCategory.Linear, null, null, 30, It.IsAny<CancellationToken>()), Times.Once);
        derivativesDataProvider.Verify(x => x.GetLongShortRatioHistoryAsync("BTCUSDT", MarketCategory.Linear, LongShortRatioPeriod.FiveMinutes, null, null, 50, It.IsAny<CancellationToken>()), Times.Once);
        privateAccountProvider.Verify(x => x.GetWalletBalanceAsync(AccountType.Unified, It.IsAny<CancellationToken>()), Times.Once);
        privateAccountProvider.Verify(x => x.GetOpenPositionsAsync(MarketCategory.Linear, "BTCUSDT", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CollectAsync_Skips_Derivatives_Only_Requests_For_Spot_Market()
    {
        var marketDataProvider = new Mock<IMarketDataProvider>();
        var derivativesDataProvider = new Mock<IDerivativesDataProvider>();
        var privateAccountProvider = new Mock<IPrivateAccountProvider>();

        marketDataProvider.Setup(x => x.GetTickerAsync("BTCUSDT", MarketCategory.Spot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTicker(category: MarketCategory.Spot, markPrice: 0m, indexPrice: 0m, fundingRate: null, openInterest: null, openInterestValue: null));
        marketDataProvider.Setup(x => x.GetOrderBookAsync("BTCUSDT", MarketCategory.Spot, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOrderBook(category: MarketCategory.Spot));
        marketDataProvider.Setup(x => x.GetRecentTradesAsync("BTCUSDT", MarketCategory.Spot, 60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { CreateTrade(category: MarketCategory.Spot) });
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Spot, KlineInterval.FifteenMinutes, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKlines(KlineInterval.FifteenMinutes, 5, MarketCategory.Spot));
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Spot, KlineInterval.OneHour, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKlines(KlineInterval.OneHour, 5, MarketCategory.Spot));
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Spot, KlineInterval.FourHours, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKlines(KlineInterval.FourHours, 5, MarketCategory.Spot));
        marketDataProvider.Setup(x => x.GetKlinesAsync("BTCUSDT", MarketCategory.Spot, KlineInterval.OneDay, null, null, 250, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateKlines(KlineInterval.OneDay, 5, MarketCategory.Spot));
        privateAccountProvider.Setup(x => x.GetWalletBalanceAsync(AccountType.Unified, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountBalance?)null);

        var collector = new MarketDataCollector(marketDataProvider.Object, derivativesDataProvider.Object, privateAccountProvider.Object);

        var result = await collector.CollectAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Spot);

        result.OpenInterestEntries.Should().BeEmpty();
        result.FundingRateEntries.Should().BeEmpty();
        result.LongShortRatioEntries.Should().BeEmpty();
        result.OpenPositions.Should().BeEmpty();

        derivativesDataProvider.Verify(x => x.GetOpenInterestHistoryAsync(It.IsAny<string>(), It.IsAny<MarketCategory>(), It.IsAny<OpenInterestInterval>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        derivativesDataProvider.Verify(x => x.GetFundingRateHistoryAsync(It.IsAny<string>(), It.IsAny<MarketCategory>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        derivativesDataProvider.Verify(x => x.GetLongShortRatioHistoryAsync(It.IsAny<string>(), It.IsAny<MarketCategory>(), It.IsAny<LongShortRatioPeriod>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        privateAccountProvider.Verify(x => x.GetOpenPositionsAsync(It.IsAny<MarketCategory>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        privateAccountProvider.Verify(x => x.GetWalletBalanceAsync(AccountType.Unified, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static MarketDataCollector CreateCollector() =>
        new(
            new Mock<IMarketDataProvider>().Object,
            new Mock<IDerivativesDataProvider>().Object,
            new Mock<IPrivateAccountProvider>().Object);

    private static Ticker CreateTicker(
        MarketCategory category = MarketCategory.Linear,
        decimal markPrice = 101m,
        decimal indexPrice = 99m,
        decimal? fundingRate = 0.0002m,
        decimal? openInterest = 1_200m,
        decimal? openInterestValue = 120_000m) =>
        new(
            "BTCUSDT",
            category,
            100m,
            markPrice,
            indexPrice,
            99.5m,
            10m,
            100.5m,
            12m,
            0.01m,
            110m,
            90m,
            1_000_000m,
            100_000_000m)
        {
            FundingRate = fundingRate,
            NextFundingTimeUtc = DateTimeOffset.UtcNow.AddHours(4),
            OpenInterest = openInterest,
            OpenInterestValue = openInterestValue,
        };

    private static OrderBook CreateOrderBook(MarketCategory category = MarketCategory.Linear) =>
        new(
            "BTCUSDT",
            category,
            DateTimeOffset.UtcNow,
            [new OrderBookEntry(99.5m, 10m), new OrderBookEntry(99m, 8m)],
            [new OrderBookEntry(100.5m, 12m), new OrderBookEntry(101m, 9m)]);

    private static Trade CreateTrade(MarketCategory category = MarketCategory.Linear) =>
        new("BTCUSDT", category, DateTimeOffset.UtcNow, TradeSide.Buy, 5m, 100m);

    private static Kline[] CreateKlines(KlineInterval interval, int count, MarketCategory category = MarketCategory.Linear)
    {
        var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var step = interval switch
        {
            KlineInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            KlineInterval.OneHour => TimeSpan.FromHours(1),
            KlineInterval.FourHours => TimeSpan.FromHours(4),
            KlineInterval.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1),
        };

        return Enumerable.Range(0, count)
            .Select(i => new Kline(
                "BTCUSDT",
                category,
                interval,
                start.Add(step * i),
                100m + i,
                101m + i,
                99m + i,
                100.5m + i,
                1_000m + i,
                100_000m + i))
            .ToArray();
    }

    private static OpenInterestEntry CreateOpenInterestEntry() =>
        new("BTCUSDT", MarketCategory.Linear, DateTimeOffset.UtcNow, 1_000m);

    private static FundingRateEntry CreateFundingRateEntry() =>
        new("BTCUSDT", MarketCategory.Linear, DateTimeOffset.UtcNow, 0.0002m);

    private static LongShortRatioEntry CreateLongShortRatioEntry() =>
        new("BTCUSDT", MarketCategory.Linear, DateTimeOffset.UtcNow, 0.55m, 0.45m);

    private static AccountBalance CreateBalance() =>
        new(AccountType.Unified, 10_000m, 9_500m, 8_000m, 500m, []);

    private static OpenPosition CreatePosition() =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            PositionSide.Long,
            PositionStatus.Normal,
            1m,
            100m,
            100m,
            5m,
            101m,
            100m,
            80m,
            5m,
            null,
            null,
            null,
            1,
            100_000m,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow);
}
