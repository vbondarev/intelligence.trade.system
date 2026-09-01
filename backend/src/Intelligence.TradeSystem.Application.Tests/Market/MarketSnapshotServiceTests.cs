using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests;

public sealed class MarketSnapshotServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BuildSnapshotAsync_Throws_When_Symbol_Is_Null_Or_Whitespace(string? symbol)
    {
        var service = new MarketSnapshotService(new Mock<IPublicMarketDataCollector>().Object);

        var act = () => service.BuildSnapshotAsync(ExchangeId.Bybit, symbol!, MarketCategory.Linear);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BuildSnapshotAsync_Throws_When_Exchange_Is_Not_Supported()
    {
        var service = new MarketSnapshotService(new Mock<IPublicMarketDataCollector>().Object);

        var act = () => service.BuildSnapshotAsync((ExchangeId)999, "BTCUSDT", MarketCategory.Linear);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task BuildSnapshotAsync_Throws_When_Ticker_Is_Missing()
    {
        var collector = new Mock<IPublicMarketDataCollector>();
        collector.Setup(x => x.CollectAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCollectedData() with { Ticker = null });

        var service = new MarketSnapshotService(collector.Object);

        var act = () => service.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ticker*");
    }

    [Fact]
    public async Task BuildSnapshotAsync_Returns_MarketSnapshot_Without_Portfolio()
    {
        var collector = new Mock<IPublicMarketDataCollector>();
        collector.Setup(x => x.CollectAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCollectedData());

        var service = new MarketSnapshotService(collector.Object);

        var result = await service.BuildSnapshotAsync(ExchangeId.Bybit, " BTCUSDT ", MarketCategory.Linear);

        result.Exchange.Should().Be("Bybit");
        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be("linear");
        result.Price.LastPrice.Should().Be(100m);
        result.Derivatives.FundingRate.Should().Be(0.0004m);
        result.OrderBook.BestBidPrice.Should().Be(99.5m);
        result.TradeFlow.TotalTrades.Should().Be(3);
        result.M15.Timeframe.Should().Be("15m");
        result.H1.Timeframe.Should().Be("1h");
        result.H4.Timeframe.Should().Be("4h");
        result.D1.Timeframe.Should().Be("1d");
        result.Sentiment.MarketRegime.Should().NotBeNullOrWhiteSpace();
        result.Tags.Should().NotBeNull();
        typeof(MarketSnapshot).GetProperty("Portfolio").Should().BeNull();

        collector.Verify(x => x.CollectAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildSnapshotAsync_Uses_Fallbacks_When_Optional_Derivatives_Data_Are_Unavailable()
    {
        var ticker = CreateTicker();
        var collector = new Mock<IPublicMarketDataCollector>();
        collector.Setup(x => x.CollectAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCollectedData() with
            {
                FundingRateEntries = [],
                OpenInterestEntries = [],
                LongShortRatioEntries = [],
                Ticker = ticker,
            });

        var service = new MarketSnapshotService(collector.Object);

        var result = await service.BuildSnapshotAsync(ExchangeId.Bybit, "BTCUSDT", MarketCategory.Linear);

        result.Derivatives.FundingRate.Should().Be(0.0004m);
        result.Derivatives.FundingRateAvg24h.Should().Be(0.0004m);
        result.Derivatives.OpenInterestChange1hPct.Should().Be(0m);
        result.Derivatives.OpenInterestChange4hPct.Should().Be(0m);
        result.Derivatives.LongRatio.Should().Be(0m);
        result.Derivatives.ShortRatio.Should().Be(0m);
    }

    private static CollectedPublicMarketData CreateCollectedData() =>
        new()
        {
            ExchangeId = ExchangeId.Bybit,
            Symbol = "BTCUSDT",
            Category = MarketCategory.Linear,
            Ticker = CreateTicker(),
            OrderBook = CreateOrderBook(),
            Trades = CreateTrades(),
            M15Klines = CreateKlines(KlineInterval.FifteenMinutes, 30),
            H1Klines = CreateKlines(KlineInterval.OneHour, 30),
            H4Klines = CreateKlines(KlineInterval.FourHours, 30),
            D1Klines = CreateKlines(KlineInterval.OneDay, 30),
            OpenInterestEntries = CreateOpenInterestEntries(),
            OpenInterestInterval = OpenInterestInterval.FiveMinutes,
            FundingRateEntries = CreateFundingRateEntries(),
            LongShortRatioEntries = CreateLongShortRatioEntries(),
            LongShortRatioPeriod = LongShortRatioPeriod.FiveMinutes,
        };

    private static Ticker CreateTicker() =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            100m,
            101m,
            99m,
            99.5m,
            10m,
            100.5m,
            12m,
            0.015m,
            110m,
            90m,
            1_500_000m,
            150_000_000m)
        {
            FundingRate = 0.0004m,
            NextFundingTimeUtc = new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero),
            OpenInterest = 2_000m,
            OpenInterestValue = 200_000m,
        };

    private static OrderBook CreateOrderBook() =>
        new(
            "BTCUSDT",
            MarketCategory.Linear,
            new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero),
            [new OrderBookEntry(99.5m, 10m), new OrderBookEntry(99.0m, 8m), new OrderBookEntry(98.5m, 7m)],
            [new OrderBookEntry(100.5m, 12m), new OrderBookEntry(101.0m, 9m), new OrderBookEntry(101.5m, 8m)]);

    private static IReadOnlyList<Trade> CreateTrades() =>
        [
            new Trade("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 11, 55, 0, TimeSpan.Zero), TradeSide.Buy, 8m, 100m),
            new Trade("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 11, 56, 0, TimeSpan.Zero), TradeSide.Buy, 6m, 100.2m),
            new Trade("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 11, 57, 0, TimeSpan.Zero), TradeSide.Sell, 3m, 99.9m),
        ];

    private static Kline[] CreateKlines(KlineInterval interval, int count)
    {
        var start = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var step = interval switch
        {
            KlineInterval.FifteenMinutes => TimeSpan.FromMinutes(15),
            KlineInterval.OneHour => TimeSpan.FromHours(1),
            KlineInterval.FourHours => TimeSpan.FromHours(4),
            KlineInterval.OneDay => TimeSpan.FromDays(1),
            _ => TimeSpan.FromMinutes(1),
        };

        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var open = 100m + i;
                var close = open + 0.75m;
                return new Kline(
                    "BTCUSDT",
                    MarketCategory.Linear,
                    interval,
                    start.Add(step * i),
                    open,
                    close + 0.5m,
                    open - 0.5m,
                    close,
                    1_000m + (i * 25m),
                    100_000m + (i * 1_000m));
            })
            .ToArray();
    }

    private static IReadOnlyList<OpenInterestEntry> CreateOpenInterestEntries() =>
        [
            new OpenInterestEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero), 1_800m),
            new OpenInterestEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 11, 0, 0, TimeSpan.Zero), 1_900m),
            new OpenInterestEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero), 2_000m),
        ];

    private static FundingRateEntry[] CreateFundingRateEntries() =>
        Enumerable.Range(0, 6)
            .Select(i => new FundingRateEntry(
                "BTCUSDT",
                MarketCategory.Linear,
                new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero).AddHours(-8 * i),
                0.0002m + (i * 0.00001m)))
            .ToArray();

    private static IReadOnlyList<LongShortRatioEntry> CreateLongShortRatioEntries() =>
        [
            new LongShortRatioEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero), 0.54m, 0.46m),
            new LongShortRatioEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 11, 0, 0, TimeSpan.Zero), 0.56m, 0.44m),
            new LongShortRatioEntry("BTCUSDT", MarketCategory.Linear, new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero), 0.58m, 0.42m),
        ];
}
