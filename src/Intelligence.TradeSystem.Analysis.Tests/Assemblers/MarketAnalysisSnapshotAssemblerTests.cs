using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class MarketAnalysisSnapshotAssemblerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Throws_ArgumentException_When_Exchange_Is_Null_Or_Whitespace(string? exchange)
    {
        var act = () => AssembleWithDefaults(exchange: exchange!);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName(nameof(exchange));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void Throws_ArgumentException_When_Symbol_Is_Null_Or_Whitespace(string? symbol)
    {
        var act = () => AssembleWithDefaults(symbol: symbol!);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName(nameof(symbol));
    }

    [Theory]
    [InlineData("price")]
    [InlineData("derivatives")]
    [InlineData("orderBook")]
    [InlineData("tradeFlow")]
    [InlineData("m15")]
    [InlineData("h1")]
    [InlineData("h4")]
    [InlineData("d1")]
    [InlineData("sentiment")]
    [InlineData("portfolio")]
    public void Throws_ArgumentNullException_When_Required_Snapshot_Is_Null(string parameterName)
    {
        var act = CreateAssembleActWithNull(parameterName);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName(parameterName);
    }

    [Fact]
    public void Builds_Consistent_MarketAnalysisSnapshot_When_All_Inputs_Are_Provided()
    {
        var price = CreatePrice();
        var derivatives = CreateDerivatives(fundingRate: 0.0005m);
        var orderBook = CreateOrderBook(imbalanceTop5: 0.35m);
        var tradeFlow = CreateTradeFlow(hasAggressiveBuyPressure: true);
        var m15 = CreateTimeframe("15m");
        var h1 = CreateTimeframe("1h", rsiOverbought: true);
        var h4 = CreateTimeframe("4h");
        var d1 = CreateTimeframe("1d");
        var sentiment = CreateSentiment(marketRegime: "MeanReversion");
        var portfolio = CreatePortfolio();
        var before = DateTimeOffset.UtcNow;

        var result = MarketAnalysisSnapshotAssembler.Assemble(
            "Bybit",
            "BTCUSDT",
            MarketCategory.Linear,
            price,
            derivatives,
            orderBook,
            tradeFlow,
            m15,
            h1,
            h4,
            d1,
            sentiment,
            portfolio);

        var after = DateTimeOffset.UtcNow;

        result.Exchange.Should().Be("Bybit");
        result.Symbol.Should().Be("BTCUSDT");
        result.Category.Should().Be("linear");
        result.CapturedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        result.Price.Should().BeSameAs(price);
        result.Derivatives.Should().BeSameAs(derivatives);
        result.OrderBook.Should().BeSameAs(orderBook);
        result.TradeFlow.Should().BeSameAs(tradeFlow);
        result.M15.Should().BeSameAs(m15);
        result.H1.Should().BeSameAs(h1);
        result.H4.Should().BeSameAs(h4);
        result.D1.Should().BeSameAs(d1);
        result.Sentiment.Should().BeSameAs(sentiment);
        result.Portfolio.Should().BeSameAs(portfolio);

        result.Tags.Should().Equal(
            "mean-reversion-regime",
            "bid-pressure",
            "aggressive-buying",
            "positive-funding",
            "rsi-overbought",
            "range-bound",
            "neutral-timeframes",
            "weak-trend");
    }

    [Theory]
    [InlineData(MarketCategory.Spot, "spot")]
    [InlineData(MarketCategory.Linear, "linear")]
    [InlineData(MarketCategory.Inverse, "inverse")]
    public void Normalizes_All_MarketCategory_Values_To_Lowercase_String(MarketCategory category, string expected)
    {
        var result = AssembleWithDefaults(category: category);

        result.Category.Should().Be(expected);
    }

    [Fact]
    public void Does_Not_Add_Regime_Tag_For_Non_Whitelisted_Regime_MeanReversion()
    {
        // MeanReversion — вне V1 whitelist → тег не добавляется
        var sentiment = CreateSentiment(marketRegime: "MeanReversion");

        var result = AssembleWithDefaults(sentiment: sentiment);

        result.Tags.Should().NotContain("mean-reversion");
        result.Tags.Should().NotContain("trending");
        result.Tags.Should().NotContain("neutral");
    }

    [Fact]
    public void Adds_Trending_Regime_Tag_When_MarketRegime_Is_Trending()
    {
        var sentiment = CreateSentiment(marketRegime: "Trending");

        var result = AssembleWithDefaults(sentiment: sentiment);

        result.Tags.Should().Contain("trending");
    }

    [Fact]
    public void Does_Not_Add_Regime_Tag_When_MarketRegime_Is_Empty()
    {
        var sentiment = CreateSentiment(marketRegime: string.Empty);

        var result = AssembleWithDefaults(sentiment: sentiment);

        result.Tags.Should().NotContain("trending");
        result.Tags.Should().NotContain("mean-reversion");
        result.Tags.Should().NotContain("volatile");
        result.Tags.Should().NotContain("neutral");
    }

    [Fact]
    public void Adds_Positive_Funding_Tag_When_FundingRate_Is_Above_Zero_Regardless_Of_Magnitude()
    {
        // V1 не имеет "funding-spike" — любой положительный fundingRate даёт "positive-funding"
        var derivatives = CreateDerivatives(fundingRate: 0.001m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.Tags.Should().Contain("positive-funding");
        result.Tags.Should().NotContain("negative-funding");
    }

    [Theory]
    [InlineData(0.0005, "positive-funding")]
    [InlineData(-0.0005, "negative-funding")]
    public void Adds_Directional_Funding_Tag_For_NonZero_Funding_Rate(decimal fundingRate, string expectedTag)
    {
        var derivatives = CreateDerivatives(fundingRate: fundingRate);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.Tags.Should().Contain(expectedTag);
    }

    [Fact]
    public void Prefers_Aggressive_Buying_Tag_When_Both_TradeFlow_Aggression_Flags_Are_Set()
    {
        var tradeFlow = CreateTradeFlow(hasAggressiveBuyPressure: true, hasAggressiveSellPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.Tags.Should().Contain("aggressive-buying");
        result.Tags.Should().NotContain("aggressive-selling");
    }

    [Fact]
    public void Adds_RSI_Tags_When_RSI_Conditions_Are_Met()
    {
        var h1 = CreateTimeframe("1h", rsiOverbought: true, rsiOversold: true);

        var result = AssembleWithDefaults(h1: h1);

        result.Tags.Should().Contain("rsi-overbought");
        result.Tags.Should().Contain("rsi-oversold");
    }

    [Fact]
    public void Adds_Bid_Pressure_Tag_When_Top5_Imbalance_Exceeds_Threshold()
    {
        var orderBook = CreateOrderBook(imbalanceTop5: 0.31m);

        var result = AssembleWithDefaults(orderBook: orderBook);

        result.Tags.Should().Contain("bid-pressure");
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(-0.3)]
    public void Does_Not_Add_OrderBook_Tag_When_Top5_Imbalance_Equals_Threshold(decimal imbalanceTop5)
    {
        var orderBook = CreateOrderBook(imbalanceTop5: imbalanceTop5);

        var result = AssembleWithDefaults(orderBook: orderBook);

        result.Tags.Should().NotContain("bid-pressure");
        result.Tags.Should().NotContain("ask-pressure");
    }

    [Fact]
    public void Uses_Only_Top5_Imbalance_For_OrderBook_Tags_And_Ignores_Deeper_Levels()
    {
        var orderBook = CreateOrderBook(
            imbalanceTop5: 0m,
            imbalanceTop10: 0.95m,
            imbalanceTop20: -0.95m);

        var result = AssembleWithDefaults(orderBook: orderBook);

        result.Tags.Should().NotContain("bid-pressure");
        result.Tags.Should().NotContain("ask-pressure");
    }

    [Fact]
    public void Returns_UnknownMarketRegime_When_No_Directional_Tag_Conditions_Are_Met()
    {
        var result = AssembleWithDefaults(sentiment: CreateSentiment(marketRegime: string.Empty));

        result.Tags.Should().Contain("unknown-market-regime");
    }

    [Fact]
    public void Builds_Expected_Tag_Set_For_Bearish_Stress_Scenario()
    {
        var derivatives = CreateDerivatives(fundingRate: -0.0012m);
        var orderBook = CreateOrderBook(imbalanceTop5: -0.45m);
        var tradeFlow = CreateTradeFlow(hasAggressiveSellPressure: true);
        var h1 = CreateTimeframe("1h", rsiOversold: true);
        var sentiment = CreateSentiment(marketRegime: "MeanReversion");

        var result = AssembleWithDefaults(
            category: MarketCategory.Inverse,
            derivatives: derivatives,
            orderBook: orderBook,
            tradeFlow: tradeFlow,
            h1: h1,
            sentiment: sentiment);

        result.Category.Should().Be("inverse");
        // V2: MeanReversion → unknown-market-regime; RSI/structure теги добавляются.
        result.Tags.Should().Equal(
            "mean-reversion-regime",
            "ask-pressure",
            "aggressive-selling",
            "negative-funding",
            "rsi-oversold",
            "range-bound",
            "neutral-timeframes",
            "weak-trend");
    }

    private static MarketAnalysisSnapshot AssembleWithDefaults(
        string exchange = "Bybit",
        string symbol = "BTCUSDT",
        MarketCategory category = MarketCategory.Linear,
        PriceSnapshot? price = null,
        DerivativesSnapshot? derivatives = null,
        OrderBookSnapshot? orderBook = null,
        TradeFlowSnapshot? tradeFlow = null,
        TimeframeAnalysisSnapshot? m15 = null,
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null,
        TimeframeAnalysisSnapshot? d1 = null,
        SentimentSnapshot? sentiment = null,
        PortfolioSnapshot? portfolio = null) =>
        MarketAnalysisSnapshotAssembler.Assemble(
            exchange,
            symbol,
            category,
            price ?? CreatePrice(),
            derivatives ?? CreateDerivatives(),
            orderBook ?? CreateOrderBook(),
            tradeFlow ?? CreateTradeFlow(),
            m15 ?? CreateTimeframe("15m"),
            h1 ?? CreateTimeframe("1h"),
            h4 ?? CreateTimeframe("4h"),
            d1 ?? CreateTimeframe("1d"),
            sentiment ?? CreateSentiment(),
            portfolio ?? CreatePortfolio());

    private static Action CreateAssembleActWithNull(string parameterName)
    {
        var price = CreatePrice();
        var derivatives = CreateDerivatives();
        var orderBook = CreateOrderBook();
        var tradeFlow = CreateTradeFlow();
        var m15 = CreateTimeframe("15m");
        var h1 = CreateTimeframe("1h");
        var h4 = CreateTimeframe("4h");
        var d1 = CreateTimeframe("1d");
        var sentiment = CreateSentiment();
        var portfolio = CreatePortfolio();

        return parameterName switch
        {
            "price" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, null!, derivatives, orderBook, tradeFlow, m15, h1, h4, d1, sentiment, portfolio),
            "derivatives" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, null!, orderBook, tradeFlow, m15, h1, h4, d1, sentiment, portfolio),
            "orderBook" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, null!, tradeFlow, m15, h1, h4, d1, sentiment, portfolio),
            "tradeFlow" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, null!, m15, h1, h4, d1, sentiment, portfolio),
            "m15" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, null!, h1, h4, d1, sentiment, portfolio),
            "h1" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, m15, null!, h4, d1, sentiment, portfolio),
            "h4" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, m15, h1, null!, d1, sentiment, portfolio),
            "d1" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, m15, h1, h4, null!, sentiment, portfolio),
            "sentiment" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, m15, h1, h4, d1, null!, portfolio),
            "portfolio" => () => MarketAnalysisSnapshotAssembler.Assemble("Bybit", "BTCUSDT", MarketCategory.Linear, price, derivatives, orderBook, tradeFlow, m15, h1, h4, d1, sentiment, null!),
            _ => throw new InvalidOperationException($"Unsupported parameter name: {parameterName}"),
        };
    }

    private static PriceSnapshot CreatePrice() =>
        new()
        {
            LastPrice = 100m,
            MarkPrice = 100m,
            IndexPrice = 100m,
            BidPrice = 99.5m,
            AskPrice = 100.5m,
            BidSize = 10m,
            AskSize = 12m,
            SpreadAbs = 1m,
            SpreadPct = 1m,
            Price24hChangePct = 0.5m,
            High24h = 105m,
            Low24h = 95m,
            Volume24h = 10_000m,
            Turnover24h = 1_000_000m,
        };

    private static DerivativesSnapshot CreateDerivatives(decimal fundingRate = 0m) =>
        new()
        {
            FundingRate = fundingRate,
            FundingRateAvg24h = 0m,
            OpenInterest = 1_000m,
            OpenInterestValue = 100_000m,
            LongRatio = 0.5m,
            ShortRatio = 0.5m,
            PremiumVsIndexPct = 0m,
            OpenInterestChange1hPct = 0m,
            OpenInterestChange4hPct = 0m,
        };

    private static OrderBookSnapshot CreateOrderBook(
        decimal imbalanceTop5 = 0m,
        decimal imbalanceTop10 = 0m,
        decimal imbalanceTop20 = 0m) =>
        new()
        {
            CapturedAtUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            BestBidPrice = 99.5m,
            BestAskPrice = 100.5m,
            TotalBidVolumeTop5 = 10m,
            TotalAskVolumeTop5 = 10m,
            TotalBidVolumeTop10 = 20m,
            TotalAskVolumeTop10 = 20m,
            TotalBidVolumeTop20 = 40m,
            TotalAskVolumeTop20 = 40m,
            ImbalanceTop5 = imbalanceTop5,
            ImbalanceTop10 = imbalanceTop10,
            ImbalanceTop20 = imbalanceTop20,
            TopBids = [],
            TopAsks = [],
            BidWalls = [],
            AskWalls = [],
        };

    private static TradeFlowSnapshot CreateTradeFlow(
        bool hasAggressiveBuyPressure = false,
        bool hasAggressiveSellPressure = false) =>
        new()
        {
            WindowStartUtc = new DateTimeOffset(2024, 1, 1, 11, 55, 0, TimeSpan.Zero),
            WindowEndUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            BuyVolume = 50m,
            SellVolume = 50m,
            DeltaVolume = 0m,
            DeltaPct = 0m,
            TotalTrades = 10,
            BuyTrades = 5,
            SellTrades = 5,
            AvgTradeSize = 10m,
            MaxTradeSize = 25m,
            HasAggressiveBuyPressure = hasAggressiveBuyPressure,
            HasAggressiveSellPressure = hasAggressiveSellPressure,
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(
        string timeframe,
        bool rsiOverbought = false,
        bool rsiOversold = false) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1_000m,
                Turnover = 100_000m,
            },
            Ema20 = 100m,
            Ema50 = 100m,
            Ema200 = 100m,
            Rsi14 = 50m,
            Rsi14IsReliable = true,
            Atr14 = 1m,
            VolumeSma20 = 1_000m,
            VolumeRatio = 1m,
            TrendStrengthScore = 0m,
            Trend = MarketTrend.Unknown,
            Support1 = 95m,
            Support2 = 90m,
            Resistance1 = 105m,
            Resistance2 = 110m,
            IsAboveEma20 = false,
            IsAboveEma50 = false,
            IsAboveEma200 = false,
            EmaBullishAlignment = false,
            EmaBearishAlignment = false,
            RsiOverbought = rsiOverbought,
            RsiOversold = rsiOversold,
            CandleRangePct = 2m,
            DistanceToSupport1Pct = 5m,
            DistanceToResistance1Pct = 5m,
        };

    private static SentimentSnapshot CreateSentiment(string marketRegime = "") =>
        new()
        {
            LongShortBiasScore = 0m,
            FundingBiasScore = 0m,
            OrderBookPressureScore = 0m,
            TradeFlowPressureScore = 0m,
            MarketRegime = marketRegime,
        };

    private static PortfolioSnapshot CreatePortfolio() =>
        new()
        {
            TotalEquityUsd = 10_000m,
            AvailableBalanceUsd = 7_500m,
            TotalWalletBalanceUsd = 9_800m,
            TotalUnrealizedPnlUsd = 200m,
            OpenPositions = [],
        };
}
