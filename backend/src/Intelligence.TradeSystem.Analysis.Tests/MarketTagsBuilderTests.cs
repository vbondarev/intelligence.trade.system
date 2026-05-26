using FluentAssertions;
using Intelligence.TradeSystem.Analysis;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests;

/// <summary>
/// Unit-тесты для <see cref="MarketTagsBuilder"/>.
/// Проверяют детерминированность, приоритет и ключевые правила V2.
/// </summary>
public sealed class MarketTagsBuilderTests
{
    // ─── 4.1 Regime tags ─────────────────────────────────────────────────────

    [Fact]
    public void Trending_Regime_Produces_Trending_Tag()
    {
        var result = Build(sentiment: CreateSentiment("Trending"));

        result.Should().Contain(MarketTagsBuilder.TagTrending);
        result.Should().NotContain(MarketTagsBuilder.TagNeutral);
    }

    [Fact]
    public void Neutral_Regime_Produces_Neutral_Tag()
    {
        var result = Build(sentiment: CreateSentiment("Neutral"));

        result.Should().Contain(MarketTagsBuilder.TagNeutral);
        result.Should().NotContain(MarketTagsBuilder.TagTrending);
    }

    [Theory]
    [InlineData("Volatile")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void Non_Whitelisted_Regime_Produces_No_Regime_Tag(string regime)
    {
        var result = Build(sentiment: CreateSentiment(regime));

        result.Should().NotContain(MarketTagsBuilder.TagTrending);
        result.Should().NotContain(MarketTagsBuilder.TagNeutral);
    }

    [Fact]
    public void GetRegimeTag_Returns_Trending_For_Trending()
    {
        MarketTagsBuilder.GetRegimeTag("Trending").Should().Be(MarketTagsBuilder.TagTrending);
    }

    [Fact]
    public void GetRegimeTag_Returns_Neutral_For_Neutral()
    {
        MarketTagsBuilder.GetRegimeTag("Neutral").Should().Be(MarketTagsBuilder.TagNeutral);
    }

    [Fact]
    public void GetRegimeTag_Returns_Null_For_MeanReversion()
    {
        MarketTagsBuilder.GetRegimeTag("MeanReversion").Should().BeNull();
    }

    // ─── MeanReversion regime ─────────────────────────────────────────────────

    [Fact]
    public void MeanReversion_Regime_Produces_MeanReversionRegime_Tag()
    {
        var result = Build(sentiment: CreateSentiment("MeanReversion"));

        result.Should().Contain(MarketTagsBuilder.TagMeanReversionRegime);
        result.Should().NotContain(MarketTagsBuilder.TagUnknownMarketRegime);
    }

    [Theory]
    [InlineData(" meanreversion ")]
    [InlineData(" MEANREVERSION ")]
    [InlineData("MeanReversion")]
    public void MeanReversion_Regime_Is_Trim_And_Case_Insensitive(string regime)
    {
        var result = Build(sentiment: CreateSentiment(regime));

        result.Should().Contain(MarketTagsBuilder.TagMeanReversionRegime);
        result.Should().NotContain(MarketTagsBuilder.TagUnknownMarketRegime);
    }

    [Fact]
    public void Null_Regime_Produces_UnknownMarketRegime_Tag()
    {
        var result = Build(sentiment: CreateSentiment(null!));

        result.Should().Contain(MarketTagsBuilder.TagUnknownMarketRegime);
        result.Should().NotContain(MarketTagsBuilder.TagMeanReversionRegime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Or_Whitespace_Regime_Produces_UnknownMarketRegime_Tag(string regime)
    {
        var result = Build(sentiment: CreateSentiment(regime));

        result.Should().Contain(MarketTagsBuilder.TagUnknownMarketRegime);
        result.Should().NotContain(MarketTagsBuilder.TagMeanReversionRegime);
    }

    [Fact]
    public void Truly_Unknown_Regime_Produces_UnknownMarketRegime_Tag()
    {
        var result = Build(sentiment: CreateSentiment("SomeUnexpectedRegime"));

        result.Should().Contain(MarketTagsBuilder.TagUnknownMarketRegime);
        result.Should().NotContain(MarketTagsBuilder.TagMeanReversionRegime);
    }

    [Theory]
    [InlineData("Volatile", "volatile-regime")]
    [InlineData("Trending", "trending")]
    [InlineData("Neutral", "neutral")]
    [InlineData("Bullish", "bullish-regime")]
    [InlineData("Bearish", "bearish-regime")]
    public void Existing_Regime_Mappings_Still_Work(string regime, string expectedTag)
    {
        var result = Build(sentiment: CreateSentiment(regime));

        result.Should().Contain(expectedTag);
        result.Should().NotContain(MarketTagsBuilder.TagUnknownMarketRegime);
        result.Should().NotContain(MarketTagsBuilder.TagMeanReversionRegime);
    }

    // ─── 4.2 Funding tags ────────────────────────────────────────────────────

    [Fact]
    public void Positive_FundingRate_Produces_PositiveFunding_Tag()
    {
        var result = Build(derivatives: CreateDerivatives(0.001m));

        result.Should().Contain(MarketTagsBuilder.TagPositiveFunding);
        result.Should().NotContain(MarketTagsBuilder.TagNegativeFunding);
    }

    [Fact]
    public void Negative_FundingRate_Produces_NegativeFunding_Tag()
    {
        var result = Build(derivatives: CreateDerivatives(-0.0005m));

        result.Should().Contain(MarketTagsBuilder.TagNegativeFunding);
        result.Should().NotContain(MarketTagsBuilder.TagPositiveFunding);
    }

    [Fact]
    public void Zero_FundingRate_Produces_No_Funding_Tag()
    {
        var result = Build(derivatives: CreateDerivatives(0m));

        result.Should().NotContain(MarketTagsBuilder.TagPositiveFunding);
        result.Should().NotContain(MarketTagsBuilder.TagNegativeFunding);
        result.Should().Contain(MarketTagsBuilder.TagNeutralFunding);
    }

    [Fact]
    public void GetFundingTag_Returns_Null_For_Zero()
    {
        MarketTagsBuilder.GetFundingTag(0m).Should().BeNull();
    }

    // ─── 4.3 Pressure tags ───────────────────────────────────────────────────

    [Fact]
    public void BidDominant_ImbalanceTop5_Produces_BidPressure_Tag()
    {
        // > 0.3 → bid-pressure
        var result = Build(orderBook: CreateOrderBook(0.35m));

        result.Should().Contain(MarketTagsBuilder.TagBidPressure);
        result.Should().NotContain(MarketTagsBuilder.TagAskPressure);
    }

    [Fact]
    public void AskDominant_ImbalanceTop5_Produces_AskPressure_Tag()
    {
        // < -0.3 → ask-pressure
        var result = Build(orderBook: CreateOrderBook(-0.4m));

        result.Should().Contain(MarketTagsBuilder.TagAskPressure);
        result.Should().NotContain(MarketTagsBuilder.TagBidPressure);
    }

    [Theory]
    [InlineData(0.3)]   // равно порогу — NOT >
    [InlineData(-0.3)]  // равно порогу — NOT <
    [InlineData(0.0)]
    public void Imbalance_At_Or_Within_Threshold_Produces_No_Pressure_Tag(decimal imbalanceTop5)
    {
        var result = Build(orderBook: CreateOrderBook(imbalanceTop5));

        result.Should().NotContain(MarketTagsBuilder.TagBidPressure);
        result.Should().NotContain(MarketTagsBuilder.TagAskPressure);
    }

    [Fact]
    public void GetPressureTag_Returns_Null_At_Exact_Threshold()
    {
        MarketTagsBuilder.GetPressureTag(MarketTagsBuilder.OrderBookPressureThreshold).Should().BeNull();
        MarketTagsBuilder.GetPressureTag(-MarketTagsBuilder.OrderBookPressureThreshold).Should().BeNull();
    }

    // ─── 4.4 Aggression tags ───────────────────────────────────────────────

    [Fact]
    public void AggressiveBuy_Flag_Produces_AggressiveBuying_Tag()
    {
        var result = Build(tradeFlow: CreateTradeFlow(hasBuy: true, hasSell: false));

        result.Should().Contain(MarketTagsBuilder.TagAggressiveBuying);
        result.Should().NotContain(MarketTagsBuilder.TagAggressiveSelling);
    }

    [Fact]
    public void AggressiveSell_Flag_Produces_AggressiveSelling_Tag()
    {
        var result = Build(tradeFlow: CreateTradeFlow(hasBuy: false, hasSell: true));

        result.Should().Contain(MarketTagsBuilder.TagAggressiveSelling);
        result.Should().NotContain(MarketTagsBuilder.TagAggressiveBuying);
    }

    [Fact]
    public void No_Aggression_Flags_Produces_No_Aggression_Tag()
    {
        var result = Build(tradeFlow: CreateTradeFlow(hasBuy: false, hasSell: false));

        result.Should().NotContain(MarketTagsBuilder.TagAggressiveBuying);
        result.Should().NotContain(MarketTagsBuilder.TagAggressiveSelling);
    }

    [Fact]
    public void BuyPressure_Has_Priority_Over_SellPressure_When_Both_Flags_Are_Set()
    {
        var result = Build(tradeFlow: CreateTradeFlow(hasBuy: true, hasSell: true));

        result.Should().Contain(MarketTagsBuilder.TagAggressiveBuying);
        result.Should().NotContain(MarketTagsBuilder.TagAggressiveSelling);
    }

    // ─── Конфликты взаимоисключающих пар ─────────────────────────────────────

    [Fact]
    public void Trending_And_Neutral_Never_Appear_Together()
    {
        var result = Build(sentiment: CreateSentiment("Trending"));

        var hasConflict = result.Contains(MarketTagsBuilder.TagTrending) &&
                          result.Contains(MarketTagsBuilder.TagNeutral);
        hasConflict.Should().BeFalse(because: "trending и neutral — взаимоисключающие теги");
    }

    [Fact]
    public void PositiveFunding_And_NegativeFunding_Never_Appear_Together()
    {
        var result = Build(derivatives: CreateDerivatives(0.001m));

        var hasConflict = result.Contains(MarketTagsBuilder.TagPositiveFunding) &&
                          result.Contains(MarketTagsBuilder.TagNegativeFunding);
        hasConflict.Should().BeFalse(because: "positive-funding и negative-funding — взаимоисключающие теги");
    }

    [Fact]
    public void BidPressure_And_AskPressure_Never_Appear_Together()
    {
        var result = Build(orderBook: CreateOrderBook(0.5m));

        var hasConflict = result.Contains(MarketTagsBuilder.TagBidPressure) &&
                          result.Contains(MarketTagsBuilder.TagAskPressure);
        hasConflict.Should().BeFalse(because: "bid-pressure и ask-pressure — взаимоисключающие теги");
    }

    [Fact]
    public void AggressiveBuying_And_AggressiveSelling_Never_Appear_Together()
    {
        var result = Build(tradeFlow: CreateTradeFlow(hasBuy: true, hasSell: true));

        var hasConflict = result.Contains(MarketTagsBuilder.TagAggressiveBuying) &&
                          result.Contains(MarketTagsBuilder.TagAggressiveSelling);
        hasConflict.Should().BeFalse(because: "aggressive-buying и aggressive-selling — взаимоисключающие теги");
    }

    // ─── Порядок тегов ────────────────────────────────────────────────────────

    [Fact]
    public void Tags_Order_Is_Regime_Funding_Pressure_Aggression()
    {
        var result = Build(
            sentiment: CreateSentiment("Trending"),
            derivatives: CreateDerivatives(0.0005m),
            orderBook: CreateOrderBook(0.5m),
            tradeFlow: CreateTradeFlow(hasBuy: true));

        // В V2 funding идёт после directional pressure/aggression.
        result.Should().Equal(
            MarketTagsBuilder.TagTrending,
            MarketTagsBuilder.TagBidPressure,
            MarketTagsBuilder.TagAggressiveBuying,
            MarketTagsBuilder.TagPositiveFunding);
    }

    [Fact]
    public void Tags_Order_Is_Stable_For_Bearish_Scenario()
    {
        var result = Build(
            sentiment: CreateSentiment("Neutral"),
            derivatives: CreateDerivatives(-0.0008m),
            orderBook: CreateOrderBook(-0.5m),
            tradeFlow: CreateTradeFlow(hasSell: true));

        result.Should().Equal(
            MarketTagsBuilder.TagNeutral,
            MarketTagsBuilder.TagAskPressure,
            MarketTagsBuilder.TagAggressiveSelling,
            MarketTagsBuilder.TagNegativeFunding);
    }

    // ─── Лимит тегов ─────────────────────────────────────────────────────────

    [Fact]
    public void Tags_Count_Does_Not_Exceed_MaxTags()
    {
        // Все 4 группы срабатывают → ровно MaxTags тегов
        var result = Build(
            sentiment: CreateSentiment("Trending"),
            derivatives: CreateDerivatives(0.001m),
            orderBook: CreateOrderBook(0.5m),
            tradeFlow: CreateTradeFlow(hasBuy: true));

        result.Count.Should().BeInRange(0, MarketTagsBuilder.MaxTags);
    }

    [Fact]
    public void Tags_Are_Empty_When_No_Conditions_Are_Met()
    {
        var result = Build(
            sentiment: CreateSentiment(""),
            derivatives: CreateDerivatives(0m),
            orderBook: CreateOrderBook(0m),
            tradeFlow: CreateTradeFlow());

        result.Should().Contain(MarketTagsBuilder.TagUnknownMarketRegime);
        result.Should().Contain(MarketTagsBuilder.TagNeutralFunding);
    }

    [Fact]
    public void TradeFlow_Quality_Adds_Conflict_And_WeakConfirmation_Tags()
    {
        var result = Build(
            tradeFlow: CreateTradeFlow(hasBuy: false, hasSell: true) with { BuyVolume = 0.4m, SellVolume = 0.3m },
            sentiment: CreateSentiment("Trending") with
            {
                OrderBookPressureScore = 0.9m,
                TradeFlowPressureScore = -0.8m,
            });

        result.Should().Contain(MarketTagsBuilder.TagLowTradeFlowVolume);
        result.Should().Contain(MarketTagsBuilder.TagOrderBookTradeFlowConflict);
        result.Should().Contain(MarketTagsBuilder.TagWeakTradeFlowConfirmation);
    }

    [Fact]
    public void MarketRegime_Is_Trimmed_Before_Matching()
    {
        var result = Build(sentiment: CreateSentiment("  Neutral  "));

        result.Should().Contain(MarketTagsBuilder.TagNeutral);
        result.Should().NotContain(MarketTagsBuilder.TagUnknownMarketRegime);
    }

    [Fact]
    public void Price_Close_To_24h_High_Adds_Near24hHigh_Tag()
    {
        var price = new PriceSnapshot
        {
            LastPrice = 100m,
            High24h = 100.2m,
            Low24h = 90m,
        };

        var result = Build(price: price, sentiment: CreateSentiment("Trending"));
        result.Should().Contain(MarketTagsBuilder.TagNear24hHigh);
    }

    // ─── Сценарий 3: volatile market regime ──────────────────────────────────

    [Fact]
    public void MarketRegime_Volatile_Adds_VolatileRegime_Tag()
    {
        var result = Build(sentiment: CreateSentiment("Volatile"));

        result.Should().Contain(MarketTagsBuilder.TagVolatileRegime);
        result.Should().NotContain(MarketTagsBuilder.TagNeutral);
        result.Should().NotContain(MarketTagsBuilder.TagTrending);
        result.Should().NotContain(MarketTagsBuilder.TagBullishRegime);
        result.Should().NotContain(MarketTagsBuilder.TagBearishRegime);
    }

    // ─── Сценарий 4: declining OI ────────────────────────────────────────────

    [Fact]
    public void Derivatives_DecliningOI_Adds_OiDeclining_Tag()
    {
        var derivatives = CreateDerivatives() with
        {
            OpenInterestChange1hPct = -0.05m,
            OpenInterestChange4hPct = -0.08m,
        };

        var result = Build(derivatives: derivatives);

        result.Should().Contain(MarketTagsBuilder.TagOiDeclining);
        result.Should().NotContain(MarketTagsBuilder.TagOiRising);
    }

    // ─── Сценарий 5: possible long unwinding ─────────────────────────────────

    [Fact]
    public void AggressiveSelling_With_DecliningOI_Adds_PossibleLongUnwinding_Tag()
    {
        var derivatives = CreateDerivatives() with
        {
            OpenInterestChange1hPct = -0.05m,
            OpenInterestChange4hPct = -0.08m,
        };

        var result = Build(
            derivatives: derivatives,
            tradeFlow: CreateTradeFlow(hasBuy: false, hasSell: true));

        result.Should().Contain(MarketTagsBuilder.TagAggressiveSelling);
        result.Should().Contain(MarketTagsBuilder.TagOiDeclining);
        result.Should().Contain(MarketTagsBuilder.TagPossibleLongUnwinding);
        result.Should().NotContain(MarketTagsBuilder.TagPossibleShortCovering);
    }

    // ─── Сценарий 6: primary timeframe low volume ─────────────────────────────

    [Fact]
    public void PrimaryTimeframe_LowVolumeRatio_Adds_LowVolume_Tag()
    {
        var m15 = CreateTimeframe("15m", null, null, MarketTrend.Bullish) with
        {
            VolumeRatio = 0.1971m,
        };

        var result = Build(m15: m15, h1: null, h4: null);

        result.Should().Contain(MarketTagsBuilder.TagLowVolume);
    }

    [Fact]
    public void BetweenStrongSupportAndResistance_Does_Not_Trigger_Level_Tags_At_0_75pct()
    {
        var tf = CreateTimeframe(
            "15m",
            distanceToSupport1Pct: 0.75m,
            distanceToResistance1Pct: 0.75m,
            trend: MarketTrend.Bullish);

        var result = Build(
            sentiment: CreateSentiment("Trending"),
            m15: tf,
            h1: null,
            h4: null);

        result.Should().NotContain(MarketTagsBuilder.TagNearSupport);
        result.Should().NotContain(MarketTagsBuilder.TagNearResistance);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<string> Build(
        DerivativesSnapshot? derivatives = null,
        OrderBookSnapshot? orderBook = null,
        TradeFlowSnapshot? tradeFlow = null,
        SentimentSnapshot? sentiment = null,
        PriceSnapshot? price = null,
        TimeframeAnalysisSnapshot? m15 = null,
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null) =>
        MarketTagsBuilder.Build(
            derivatives ?? CreateDerivatives(),
            orderBook ?? CreateOrderBook(),
            tradeFlow ?? CreateTradeFlow(),
            sentiment ?? CreateSentiment(),
            price,
            m15,
            h1,
            h4);

    private static SentimentSnapshot CreateSentiment(string marketRegime = "") =>
        new()
        {
            MarketRegime = marketRegime,
            LongShortBiasScore = 0m,
            FundingBiasScore = 0m,
            OrderBookPressureScore = 0m,
            TradeFlowPressureScore = 0m,
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

    private static OrderBookSnapshot CreateOrderBook(decimal imbalanceTop5 = 0m) =>
        new()
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            BestBidPrice = 99.5m,
            BestAskPrice = 100.5m,
            TotalBidVolumeTop5 = 10m,
            TotalAskVolumeTop5 = 10m,
            TotalBidVolumeTop10 = 20m,
            TotalAskVolumeTop10 = 20m,
            TotalBidVolumeTop20 = 40m,
            TotalAskVolumeTop20 = 40m,
            ImbalanceTop5 = imbalanceTop5,
            ImbalanceTop10 = 0m,
            ImbalanceTop20 = 0m,
            TopBids = [],
            TopAsks = [],
            BidWalls = [],
            AskWalls = [],
        };

    private static TradeFlowSnapshot CreateTradeFlow(
        bool hasBuy = false,
        bool hasSell = false) =>
        new()
        {
            WindowStartUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
            WindowEndUtc = DateTimeOffset.UtcNow,
            BuyVolume = 50m,
            SellVolume = 50m,
            DeltaVolume = 0m,
            DeltaPct = 0m,
            TotalTrades = 10,
            BuyTrades = 5,
            SellTrades = 5,
            AvgTradeSize = 10m,
            MaxTradeSize = 25m,
            HasAggressiveBuyPressure = hasBuy,
            HasAggressiveSellPressure = hasSell,
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(
        string timeframe,
        decimal? distanceToSupport1Pct,
        decimal? distanceToResistance1Pct,
        MarketTrend trend) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = DateTimeOffset.UtcNow,
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = DateTimeOffset.UtcNow,
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 100m,
                Turnover = 10_000m,
            },
            Ema20 = 100m,
            Ema50 = 100m,
            Ema200 = 100m,
            Rsi14 = 55m,
            Rsi14IsReliable = true,
            Atr14 = 1m,
            VolumeSma20 = 100m,
            VolumeRatio = 1m,
            VolumeRatioIsReliable = true,
            TrendStrengthScore = 0.6m,
            Trend = trend,
            Support1 = 99m,
            Resistance1 = 101m,
            DistanceToSupport1Pct = distanceToSupport1Pct,
            DistanceToResistance1Pct = distanceToResistance1Pct,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = true,
            EmaBullishAlignment = true,
            EmaBearishAlignment = false,
            RsiOverbought = false,
            RsiOversold = false,
        };
}
