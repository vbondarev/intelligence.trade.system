using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analytics.Tests;

public sealed class MarketRegimeClassifierTests
{
    private readonly MarketRegimeClassifier _classifier = new();

    [Fact]
    public void Throws_When_Snapshot_Is_Null()
    {
        var action = () => _classifier.Classify(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Throws_When_H1_Is_Null()
    {
        var snapshot = CreateSnapshot() with { H1 = null! };

        var action = () => _classifier.Classify(snapshot);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("h1");
    }

    [Fact]
    public void Throws_When_H4_Is_Null()
    {
        var snapshot = CreateSnapshot() with { H4 = null! };

        var action = () => _classifier.Classify(snapshot);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("h4");
    }

    [Fact]
    public void Returns_Trending_When_Directions_Are_Aligned_And_Average_Strength_Equals_Threshold()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.70m },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.50m });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Returns_Trending_For_Aligned_Bearish_Timeframes_With_Sufficient_Strength()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.65m },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.80m });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Does_Not_Return_Trending_When_Average_Strength_Is_Below_Threshold()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.59m },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.59m });

        var result = _classifier.Classify(snapshot);

        result.Should().NotBe(MarketRegimes.Trending);
        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Returns_Volatile_When_Timeframes_Have_Conflicting_Directions()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.90m },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Bearish, TrendStrengthScore = 0.90m });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Returns_Volatile_When_VolumeRatio_Exceeds_Threshold()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { VolumeRatio = 2.01m },
            h4: CreateTimeframe("4h"));

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Does_Not_Return_Volatile_When_VolumeRatio_Equals_Threshold_And_No_Other_Conditions_Apply()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { VolumeRatio = 2.0m },
            h4: CreateTimeframe("4h") with { VolumeRatio = 2.0m });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Returns_MeanReversion_When_Both_Timeframes_Are_Sideways()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Sideways },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Sideways });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.MeanReversion);
    }

    [Fact]
    public void Returns_MeanReversion_When_Any_Timeframe_Has_Rsi_Extreme()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { RsiOverbought = true },
            h4: CreateTimeframe("4h"));

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.MeanReversion);
    }

    [Fact]
    public void Returns_Neutral_When_No_Regime_Conditions_Are_Met()
    {
        var result = _classifier.Classify(CreateSnapshot());

        result.Should().Be(MarketRegimes.Neutral);
    }

    [Fact]
    public void Prefers_Trending_Over_Volatile_And_MeanReversion_When_Multiple_Regime_Conditions_Are_True()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with
            {
                Trend = MarketTrend.Bullish,
                TrendStrengthScore = 0.70m,
                VolumeRatio = 3.00m,
                RsiOverbought = true,
            },
            h4: CreateTimeframe("4h") with
            {
                Trend = MarketTrend.Bullish,
                TrendStrengthScore = 0.70m,
                RsiOversold = true,
            });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Trending);
    }

    [Fact]
    public void Prefers_Volatile_Over_MeanReversion_When_Both_Regime_Conditions_Are_True()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with
            {
                Trend = MarketTrend.Bullish,
                RsiOverbought = true,
            },
            h4: CreateTimeframe("4h") with
            {
                Trend = MarketTrend.Bearish,
                RsiOversold = true,
            });

        var result = _classifier.Classify(snapshot);

        result.Should().Be(MarketRegimes.Volatile);
    }

    [Fact]
    public void Returns_Deterministic_Result_For_Same_Input()
    {
        var snapshot = CreateSnapshot(
            h1: CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.75m },
            h4: CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.85m });

        var first = _classifier.Classify(snapshot);
        var second = _classifier.Classify(snapshot);

        second.Should().Be(first);
    }

    private static MarketAnalysisSnapshot CreateSnapshot(
        TimeframeAnalysisSnapshot? h1 = null,
        TimeframeAnalysisSnapshot? h4 = null) =>
        new()
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "linear",
            CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 10, 0, 0, TimeSpan.Zero),
            Price = new PriceSnapshot
            {
                LastPrice = 65000m,
                MarkPrice = 64990m,
                IndexPrice = 64980m,
                BidPrice = 64995m,
                AskPrice = 65005m,
                BidSize = 10m,
                AskSize = 12m,
                SpreadAbs = 10m,
                SpreadPct = 0.0154m,
                Price24hChangePct = 1.2m,
                High24h = 65200m,
                Low24h = 64000m,
                Volume24h = 12345m,
                Turnover24h = 800000000m,
            },
            Derivatives = new DerivativesSnapshot
            {
                FundingRate = 0.0001m,
                FundingRateAvg24h = 0.0001m,
                OpenInterest = 100000m,
                OpenInterestValue = 6500000000m,
                LongRatio = 0.52m,
                ShortRatio = 0.48m,
                OpenInterestChange1hPct = 1.5m,
                OpenInterestChange4hPct = 3.0m,
            },
            OrderBook = new OrderBookSnapshot
            {
                CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 10, 0, 0, TimeSpan.Zero),
                BestBidPrice = 64995m,
                BestAskPrice = 65005m,
                TotalBidVolumeTop5 = 100m,
                TotalAskVolumeTop5 = 95m,
                TotalBidVolumeTop10 = 220m,
                TotalAskVolumeTop10 = 210m,
                TotalBidVolumeTop20 = 420m,
                TotalAskVolumeTop20 = 405m,
                ImbalanceTop5 = 0.02m,
                ImbalanceTop10 = 0.01m,
                ImbalanceTop20 = 0.01m,
            },
            TradeFlow = new TradeFlowSnapshot
            {
                WindowStartUtc = new DateTimeOffset(2026, 4, 12, 9, 45, 0, TimeSpan.Zero),
                WindowEndUtc = new DateTimeOffset(2026, 4, 12, 10, 0, 0, TimeSpan.Zero),
                BuyVolume = 100m,
                SellVolume = 98m,
                DeltaVolume = 2m,
                DeltaPct = 1.01m,
                TotalTrades = 100,
                BuyTrades = 52,
                SellTrades = 48,
                AvgTradeSize = 1.98m,
                MaxTradeSize = 5m,
            },
            M15 = CreateTimeframe("15m"),
            H1 = h1 ?? CreateTimeframe("1h"),
            H4 = h4 ?? CreateTimeframe("4h"),
            D1 = CreateTimeframe("1d"),
            Sentiment = new SentimentSnapshot
            {
                LongShortBiasScore = 0.1m,
                FundingBiasScore = 0m,
                OrderBookPressureScore = 0.05m,
                TradeFlowPressureScore = 0.04m,
                MarketRegime = MarketRegimes.Neutral,
            },
            Portfolio = new PortfolioSnapshot
            {
                TotalEquityUsd = 10000m,
                AvailableBalanceUsd = 8000m,
                TotalWalletBalanceUsd = 9500m,
                TotalUnrealizedPnlUsd = 500m,
            },
            Tags = ["test"],
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(string timeframe) =>
        new()
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.Zero),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(2026, 4, 12, 9, 0, 0, TimeSpan.Zero),
                Open = 64800m,
                High = 65100m,
                Low = 64750m,
                Close = 65000m,
                Volume = 1200m,
                Turnover = 78000000m,
            },
            Ema20 = 64900m,
            Ema50 = 64850m,
            Ema200 = 64000m,
            Rsi14 = 55m,
            Atr14 = 180m,
            VolumeSma20 = 1000m,
            VolumeRatio = 1.10m,
            TrendStrengthScore = 0.40m,
            Trend = MarketTrend.Unknown,
            Support1 = 64600m,
            Support2 = 64250m,
            Resistance1 = 65200m,
            Resistance2 = 65650m,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = true,
            CandleRangePct = 0.5385m,
            DistanceToSupport1Pct = 0.6154m,
            DistanceToResistance1Pct = 0.3077m,
        };
}

