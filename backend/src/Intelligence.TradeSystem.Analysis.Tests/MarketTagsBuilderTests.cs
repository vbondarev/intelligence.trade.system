using FluentAssertions;
using Intelligence.TradeSystem.Analysis;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests;

/// <summary>
/// Unit-тесты для <see cref="MarketTagsBuilder"/>.
/// Проверяют каждое правило V1, взаимоисключения, порядок тегов и лимит.
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
    [InlineData("MeanReversion")]
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
            sentiment:   CreateSentiment("Trending"),
            derivatives: CreateDerivatives(0.0005m),
            orderBook:   CreateOrderBook(0.5m),
            tradeFlow:   CreateTradeFlow(hasBuy: true));

        result.Should().Equal(
            MarketTagsBuilder.TagTrending,
            MarketTagsBuilder.TagPositiveFunding,
            MarketTagsBuilder.TagBidPressure,
            MarketTagsBuilder.TagAggressiveBuying);
    }

    [Fact]
    public void Tags_Order_Is_Stable_For_Bearish_Scenario()
    {
        var result = Build(
            sentiment:   CreateSentiment("Neutral"),
            derivatives: CreateDerivatives(-0.0008m),
            orderBook:   CreateOrderBook(-0.5m),
            tradeFlow:   CreateTradeFlow(hasSell: true));

        result.Should().Equal(
            MarketTagsBuilder.TagNeutral,
            MarketTagsBuilder.TagNegativeFunding,
            MarketTagsBuilder.TagAskPressure,
            MarketTagsBuilder.TagAggressiveSelling);
    }

    // ─── Лимит тегов ─────────────────────────────────────────────────────────

    [Fact]
    public void Tags_Count_Does_Not_Exceed_MaxTags()
    {
        // Все 4 группы срабатывают → ровно MaxTags тегов
        var result = Build(
            sentiment:   CreateSentiment("Trending"),
            derivatives: CreateDerivatives(0.001m),
            orderBook:   CreateOrderBook(0.5m),
            tradeFlow:   CreateTradeFlow(hasBuy: true));

        result.Count.Should().BeInRange(0, MarketTagsBuilder.MaxTags);
    }

    [Fact]
    public void Tags_Are_Empty_When_No_Conditions_Are_Met()
    {
        var result = Build(
            sentiment:   CreateSentiment(""),
            derivatives: CreateDerivatives(0m),
            orderBook:   CreateOrderBook(0m),
            tradeFlow:   CreateTradeFlow());

        result.Should().BeEmpty();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static List<string> Build(
        DerivativesSnapshot? derivatives = null,
        OrderBookSnapshot?   orderBook   = null,
        TradeFlowSnapshot?   tradeFlow   = null,
        SentimentSnapshot?   sentiment   = null) =>
        MarketTagsBuilder.Build(
            derivatives ?? CreateDerivatives(),
            orderBook   ?? CreateOrderBook(),
            tradeFlow   ?? CreateTradeFlow(),
            sentiment   ?? CreateSentiment());

    private static SentimentSnapshot CreateSentiment(string marketRegime = "") =>
        new()
        {
            MarketRegime           = marketRegime,
            LongShortBiasScore     = 0m,
            FundingBiasScore       = 0m,
            OrderBookPressureScore = 0m,
            TradeFlowPressureScore = 0m,
        };

    private static DerivativesSnapshot CreateDerivatives(decimal fundingRate = 0m) =>
        new()
        {
            FundingRate             = fundingRate,
            FundingRateAvg24h       = 0m,
            OpenInterest            = 1_000m,
            OpenInterestValue       = 100_000m,
            LongRatio               = 0.5m,
            ShortRatio              = 0.5m,
            PremiumVsIndexPct       = 0m,
            OpenInterestChange1hPct = 0m,
            OpenInterestChange4hPct = 0m,
        };

    private static OrderBookSnapshot CreateOrderBook(decimal imbalanceTop5 = 0m) =>
        new()
        {
            CapturedAtUtc         = DateTimeOffset.UtcNow,
            BestBidPrice          = 99.5m,
            BestAskPrice          = 100.5m,
            TotalBidVolumeTop5    = 10m,
            TotalAskVolumeTop5    = 10m,
            TotalBidVolumeTop10   = 20m,
            TotalAskVolumeTop10   = 20m,
            TotalBidVolumeTop20   = 40m,
            TotalAskVolumeTop20   = 40m,
            ImbalanceTop5         = imbalanceTop5,
            ImbalanceTop10        = 0m,
            ImbalanceTop20        = 0m,
            TopBids               = [],
            TopAsks               = [],
            BidWalls              = [],
            AskWalls              = [],
        };

    private static TradeFlowSnapshot CreateTradeFlow(
        bool hasBuy  = false,
        bool hasSell = false) =>
        new()
        {
            WindowStartUtc            = DateTimeOffset.UtcNow.AddMinutes(-15),
            WindowEndUtc              = DateTimeOffset.UtcNow,
            BuyVolume                 = 50m,
            SellVolume                = 50m,
            DeltaVolume               = 0m,
            DeltaPct                  = 0m,
            TotalTrades               = 10,
            BuyTrades                 = 5,
            SellTrades                = 5,
            AvgTradeSize              = 10m,
            MaxTradeSize              = 25m,
            HasAggressiveBuyPressure  = hasBuy,
            HasAggressiveSellPressure = hasSell,
        };
}


