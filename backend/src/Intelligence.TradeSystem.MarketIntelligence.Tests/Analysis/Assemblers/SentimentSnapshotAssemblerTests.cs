using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;
using Xunit;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Analysis.Assemblers;

public sealed class SentimentSnapshotAssemblerTests
{
    [Theory]
    [InlineData(0, "derivatives")]
    [InlineData(1, "orderBook")]
    [InlineData(2, "tradeFlow")]
    [InlineData(3, "h1")]
    [InlineData(4, "h4")]
    public void Throws_ArgumentNullException_For_Null_Parameter(int nullParamIndex, string paramName)
    {
        Action act = nullParamIndex switch
        {
            0 => () => SentimentSnapshotAssembler.Assemble(null!, CreateOrderBook(), CreateTradeFlow(), CreateTimeframe("1h"), CreateTimeframe("4h")),
            1 => () => SentimentSnapshotAssembler.Assemble(CreateDerivatives(), null!, CreateTradeFlow(), CreateTimeframe("1h"), CreateTimeframe("4h")),
            2 => () => SentimentSnapshotAssembler.Assemble(CreateDerivatives(), CreateOrderBook(), null!, CreateTimeframe("1h"), CreateTimeframe("4h")),
            3 => () => SentimentSnapshotAssembler.Assemble(CreateDerivatives(), CreateOrderBook(), CreateTradeFlow(), null!, CreateTimeframe("4h")),
            4 => () => SentimentSnapshotAssembler.Assemble(CreateDerivatives(), CreateOrderBook(), CreateTradeFlow(), CreateTimeframe("1h"), null!),
            _ => throw new ArgumentOutOfRangeException(nameof(nullParamIndex))
        };

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName(paramName);
    }

    [Fact]
    public void Computes_LongShortBiasScore_As_LongRatio_Minus_ShortRatio()
    {
        var derivatives = CreateDerivatives(longRatio: 0.70m, shortRatio: 0.20m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.LongShortBiasScore.Should().Be(0.50m);
    }

    [Theory]
    [InlineData(1.60, 0.00, 1.0)]
    [InlineData(0.00, 1.60, -1.0)]
    public void Clamps_LongShortBiasScore_To_Valid_Range(decimal longRatio, decimal shortRatio, decimal expected)
    {
        var derivatives = CreateDerivatives(longRatio: longRatio, shortRatio: shortRatio);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.LongShortBiasScore.Should().Be(expected);
    }

    [Fact]
    public void Returns_Zero_LongShortBiasScore_When_Long_And_Short_Ratios_Are_Equal()
    {
        var derivatives = CreateDerivatives(longRatio: 0.50m, shortRatio: 0.50m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.LongShortBiasScore.Should().Be(0m);
    }

    [Fact]
    public void Computes_FundingBiasScore_As_Contrarian_Blend_Of_Current_And_Avg24h_Funding()
    {
        var derivatives = CreateDerivatives(fundingRate: 0.0005m, fundingRateAvg24h: 0.0001m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.FundingBiasScore.Should().Be(-0.3m);
    }

    [Fact]
    public void Uses_Average_Of_Current_And_Avg24h_FundingRates()
    {
        // Current funding alone would saturate to -1, but averaging with the opposite 24h value must neutralize the signal.
        var derivatives = CreateDerivatives(fundingRate: 0.001m, fundingRateAvg24h: -0.001m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.FundingBiasScore.Should().Be(0m);
    }

    [Theory]
    [InlineData(0.002, 0.002, -1.0)]
    [InlineData(-0.002, -0.002, 1.0)]
    public void Clamps_FundingBiasScore_For_Extreme_Funding(decimal fundingRate, decimal fundingRateAvg24h, decimal expected)
    {
        var derivatives = CreateDerivatives(fundingRate: fundingRate, fundingRateAvg24h: fundingRateAvg24h);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.FundingBiasScore.Should().Be(expected);
    }

    [Fact]
    public void Rounds_FundingBiasScore_To_Four_Decimals()
    {
        // Blended funding = 0.00033335, normalized by threshold 0.001 => 0.33335, then negated and rounded.
        var derivatives = CreateDerivatives(fundingRate: 0.0006667m, fundingRateAvg24h: 0m);

        var result = AssembleWithDefaults(derivatives: derivatives);

        result.FundingBiasScore.Should().Be(-0.3334m);
    }

    [Fact]
    public void Computes_OrderBookPressureScore_As_Weighted_Average_Of_Imbalances()
    {
        var orderBook = CreateOrderBook(imbalanceTop5: 0.40m, imbalanceTop10: 0.20m, imbalanceTop20: -0.50m);

        var result = AssembleWithDefaults(orderBook: orderBook);

        result.OrderBookPressureScore.Should().Be(0.16m);
    }

    [Fact]
    public void Rounds_OrderBookPressureScore_To_Four_Decimals()
    {
        // 0.3333*0.5 + 0.3333*0.3 + 0.33355*0.2 = 0.33335 -> 0.3334.
        var orderBook = CreateOrderBook(imbalanceTop5: 0.3333m, imbalanceTop10: 0.3333m, imbalanceTop20: 0.33355m);

        var result = AssembleWithDefaults(orderBook: orderBook);

        result.OrderBookPressureScore.Should().Be(0.3334m);
    }

    [Fact]
    public void Returns_Zero_OrderBookPressureScore_When_All_Imbalances_Are_Zero()
    {
        var result = AssembleWithDefaults(orderBook: CreateOrderBook());

        result.OrderBookPressureScore.Should().Be(0m);
    }

    [Fact]
    public void Computes_TradeFlowPressureScore_From_DeltaPct_When_No_Aggressive_Flags_Are_Set()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: 20m);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(0.4m);
    }

    [Theory]
    [InlineData(60, 1.0)]
    [InlineData(-75, -1.0)]
    public void Clamps_TradeFlowPressureScore_To_Valid_Range(decimal deltaPct, decimal expected)
    {
        var tradeFlow = CreateTradeFlow(deltaPct: deltaPct);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(expected);
    }

    [Fact]
    public void Applies_Aggressive_Buy_Floor_When_Normalized_Score_Is_Below_Point_Five()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: 10m, hasAggressiveBuyPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(0.5m);
    }

    [Fact]
    public void Does_Not_Reduce_TradeFlowPressureScore_When_Aggressive_Buy_Score_Is_Already_Above_Floor()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: 40m, hasAggressiveBuyPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(0.8m);
    }

    [Fact]
    public void Applies_Aggressive_Sell_Floor_When_Normalized_Score_Is_Above_Minus_Point_Five()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: -10m, hasAggressiveSellPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(-0.5m);
    }

    [Fact]
    public void Does_Not_Increase_TradeFlowPressureScore_When_Aggressive_Sell_Score_Is_Already_Below_Floor()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: -40m, hasAggressiveSellPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(-0.8m);
    }

    [Fact]
    public void Sell_Aggression_Takes_Precedence_When_Both_Aggressive_Flags_Are_Set()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: 0m, hasAggressiveBuyPressure: true, hasAggressiveSellPressure: true);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(-0.5m);
    }

    [Fact]
    public void Rounds_TradeFlowPressureScore_To_Four_Decimals()
    {
        var tradeFlow = CreateTradeFlow(deltaPct: 16.6675m);

        var result = AssembleWithDefaults(tradeFlow: tradeFlow);

        result.TradeFlowPressureScore.Should().Be(0.3334m);
    }

    [Fact]
    public void Returns_Trending_When_Directions_Are_Aligned_And_Average_Strength_Equals_Threshold()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.70m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.50m };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Trending");
    }

    [Fact]
    public void Returns_Trending_For_Aligned_Bearish_Timeframes_With_Sufficient_Strength()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.65m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.80m };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Trending");
    }

    [Fact]
    public void Returns_Volatile_When_Timeframes_Have_Conflicting_Directions()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.90m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.90m };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Volatile");
    }

    [Fact]
    public void Returns_Volatile_When_VolumeRatio_Exceeds_Threshold()
    {
        var h1 = CreateTimeframe("1h") with { VolumeRatio = 2.01m };
        var h4 = CreateTimeframe("4h");

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Volatile");
    }

    [Fact]
    public void Does_Not_Return_Volatile_When_VolumeRatio_Equals_Threshold_And_No_Other_Conditions_Apply()
    {
        var h1 = CreateTimeframe("1h") with { VolumeRatio = 2.0m };
        var h4 = CreateTimeframe("4h") with { VolumeRatio = 2.0m };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Neutral");
    }

    [Fact]
    public void Returns_MeanReversion_When_Both_Timeframes_Are_Sideways()
    {
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Sideways };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Sideways };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("MeanReversion");
    }

    [Fact]
    public void Returns_MeanReversion_When_Any_Timeframe_Has_Rsi_Extreme()
    {
        var h1 = CreateTimeframe("1h") with { RsiOverbought = true };
        var h4 = CreateTimeframe("4h");

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("MeanReversion");
    }

    [Fact]
    public void Returns_Neutral_When_No_Regime_Conditions_Are_Met()
    {
        var result = AssembleWithDefaults();

        result.MarketRegime.Should().Be("Neutral");
    }

    [Fact]
    public void Prefers_Trending_Over_Volatile_And_MeanReversion_When_Multiple_Regime_Conditions_Are_True()
    {
        var h1 = CreateTimeframe("1h") with
        {
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.70m,
            VolumeRatio = 3.00m,
            RsiOverbought = true,
        };
        var h4 = CreateTimeframe("4h") with
        {
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.70m,
            RsiOversold = true,
        };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Trending");
    }

    [Fact]
    public void Prefers_Volatile_Over_MeanReversion_When_Both_Regime_Conditions_Are_True()
    {
        var h1 = CreateTimeframe("1h") with
        {
            Trend = MarketTrend.Bullish,
            RsiOverbought = true,
        };
        var h4 = CreateTimeframe("4h") with
        {
            Trend = MarketTrend.Bearish,
            RsiOversold = true,
        };

        var result = AssembleWithDefaults(h1: h1, h4: h4);

        result.MarketRegime.Should().Be("Volatile");
    }

    [Fact]
    public void Builds_Consistent_SentimentSnapshot_When_All_Inputs_Are_Provided()
    {
        var derivatives = CreateDerivatives(
            longRatio: 0.65m,
            shortRatio: 0.35m,
            fundingRate: 0.0008m,
            fundingRateAvg24h: 0.0004m);
        var orderBook = CreateOrderBook(imbalanceTop5: 0.60m, imbalanceTop10: 0.20m, imbalanceTop20: -0.50m);
        var tradeFlow = CreateTradeFlow(deltaPct: 15m, hasAggressiveBuyPressure: true);
        var h1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.70m, VolumeRatio = 1.20m };
        var h4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.60m, VolumeRatio = 1.50m };

        var result = SentimentSnapshotAssembler.Assemble(derivatives, orderBook, tradeFlow, h1, h4);

        result.LongShortBiasScore.Should().Be(0.30m);
        result.FundingBiasScore.Should().Be(-0.60m);
        result.OrderBookPressureScore.Should().Be(0.26m);
        result.TradeFlowPressureScore.Should().Be(0.5m);
        result.MarketRegime.Should().Be("Trending");
    }

    // --- Integration: BTCUSDT-like regression --------------------------------

    /// <summary>
    /// Регрессионный тест: BTCUSDT-подобный снапшот с устаревшим tradeFlow,
    /// коротким окном, малым объёмом и конфликтом orderBook должен давать
    /// tradeFlowPressureScore &lt;= 0.25 (строгий cap).
    ///
    /// Исходные данные:
    ///   buyVolume = 0.872, sellVolume = 0.1
    ///   deltaPct ≈ 79 % → rawScore = 1.0 (clamp); HasAggressiveBuyPressure = true → floor 0.5 (raw уже выше)
    ///   windowDuration = 8 s       → windowCap = 0.25
    ///   tradeFlowAge = 5 824 ms, maxAge = 5 000 ms → staleCap = 0.50
    ///   totalVolume = 0.972 BTC    → volumeCap = 0.35
    ///   orderBook conflict + short window → conflictWithWeaknessCap = 0.25
    ///   strictest cap = 0.25
    /// </summary>
    [Fact]
    public void Integration_BtcUsdt_Like_Stale_Short_Conflict_Caps_TradeFlowScore_At_0_25()
    {
        // Arrange
        const long maxAgeMs = 5_000L; // Intraday threshold
        var now = DateTimeOffset.UtcNow;
        var windowEnd = now.AddMilliseconds(-5_824); // stale: age > maxAge
        var windowStart = windowEnd.AddSeconds(-8);  // window = 8 s < 10 s

        var buyVolume = 0.872m;
        var sellVolume = 0.1m;
        var totalVolume = buyVolume + sellVolume;    // 0.972 BTC < 1 BTC
        var deltaVolume = buyVolume - sellVolume;
        var deltaPct = deltaVolume / totalVolume * 100m; // ≈ 79.4 %

        var tradeFlow = new TradeFlowSnapshot
        {
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            BuyVolume = buyVolume,
            SellVolume = sellVolume,
            DeltaVolume = deltaVolume,
            DeltaPct = deltaPct,
            TotalTrades = 10,
            BuyTrades = 9,
            SellTrades = 1,
            AvgTradeSize = totalVolume / 10m,
            MaxTradeSize = buyVolume,
            HasAggressiveBuyPressure = true,
            HasAggressiveSellPressure = false,
        };

        // orderBook dominates ask-side → negative pressure → conflict with bullish tradeFlow
        var orderBook = CreateOrderBook(
            imbalanceTop5: -0.40m,
            imbalanceTop10: -0.20m,
            imbalanceTop20: -0.10m);

        // Act
        var result = SentimentSnapshotAssembler.Assemble(
            derivatives: CreateDerivatives(),
            orderBook: orderBook,
            tradeFlow: tradeFlow,
            h1: CreateTimeframe("1h"),
            h4: CreateTimeframe("4h"),
            capturedAtUtc: now,
            maxTradeFlowAgeMs: maxAgeMs);

        // Assert
        result.TradeFlowPressureScore
            .Should().BeGreaterThan(0m, because: "bullish raw signal must remain positive")
            .And.BeLessThanOrEqualTo(0.25m, because: "stale + short window + low volume + conflict → cap 0.25");
    }

    private static SentimentSnapshot AssembleWithDefaults(
        DerivativesSnapshot? derivatives = null,
        OrderBookSnapshot? orderBook = null,
        TradeFlowSnapshot? tradeFlow = null,
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null) =>
        SentimentSnapshotAssembler.Assemble(
            derivatives ?? CreateDerivatives(),
            orderBook ?? CreateOrderBook(),
            tradeFlow ?? CreateTradeFlow(),
            h1 ?? CreateTimeframe("1h"),
            h4 ?? CreateTimeframe("4h"));

    private static DerivativesSnapshot CreateDerivatives(
        decimal fundingRate = 0m,
        decimal fundingRateAvg24h = 0m,
        decimal longRatio = 0.50m,
        decimal shortRatio = 0.50m) =>
        new()
        {
            FundingRate = fundingRate,
            FundingRateAvg24h = fundingRateAvg24h,
            LongRatio = longRatio,
            ShortRatio = shortRatio,
            OpenInterest = 0m,
            OpenInterestValue = 0m,
            OpenInterestChange1hPct = 0m,
            OpenInterestChange4hPct = 0m,
            PremiumVsIndexPct = 0m,
            NextFundingTimeUtc = null,
        };

    private static OrderBookSnapshot CreateOrderBook(
        decimal imbalanceTop5 = 0m,
        decimal imbalanceTop10 = 0m,
        decimal imbalanceTop20 = 0m) =>
        new()
        {
            CapturedAtUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            BestBidPrice = 100m,
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
        decimal deltaPct = 0m,
        bool hasAggressiveBuyPressure = false,
        bool hasAggressiveSellPressure = false) =>
        new()
        {
            WindowStartUtc = new DateTimeOffset(2024, 1, 1, 11, 55, 0, TimeSpan.Zero),
            WindowEndUtc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            BuyVolume = 50m,
            SellVolume = 50m,
            DeltaVolume = 0m,
            DeltaPct = deltaPct,
            TotalTrades = 10,
            BuyTrades = 5,
            SellTrades = 5,
            AvgTradeSize = 10m,
            MaxTradeSize = 25m,
            HasAggressiveBuyPressure = hasAggressiveBuyPressure,
            HasAggressiveSellPressure = hasAggressiveSellPressure,
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(string timeframe) =>
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
            Atr14 = 100m,
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
            RsiOverbought = false,
            RsiOversold = false,
            CandleRangePct = 2m,
            DistanceToSupport1Pct = 5m,
            DistanceToResistance1Pct = 5m,
        };
}
