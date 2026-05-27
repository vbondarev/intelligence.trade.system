using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analytics.Tests;

public sealed class AnalyticsOutputComposerTests
{
    [Fact]
    public void Constructor_Throws_When_MarketRegimeClassifier_Is_Null()
    {
        var action = () => new AnalyticsOutputComposer(null!, new StubFormatter());

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("marketRegimeClassifier");
    }

    [Fact]
    public void Constructor_Throws_When_AnalyticsFormatter_Is_Null()
    {
        var action = () => new AnalyticsOutputComposer(new StubClassifier(MarketRegimes.Neutral), null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("analyticsFormatter");
    }

    [Fact]
    public void Compose_Throws_When_Snapshot_Is_Null()
    {
        var composer = new AnalyticsOutputComposer(new StubClassifier(MarketRegimes.Neutral), new StubFormatter());

        var action = () => composer.Compose(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("snapshot");
    }

    [Fact]
    public void Compose_Returns_MarketRegime_And_FormattedContext()
    {
        var formatter = new StubFormatter("formatted-context");
        var composer = new AnalyticsOutputComposer(new StubClassifier(MarketRegimes.Trending), formatter);

        var result = composer.Compose(CreateSnapshot());

        result.MarketRegime.Should().Be(MarketRegimes.Trending);
        result.FormattedContext.Should().Be("formatted-context");
        formatter.CapturedSnapshot.Should().NotBeNull();
    }

    [Fact]
    public void Compose_Passes_Snapshot_With_Consistent_MarketRegime_To_Formatter_Without_Mutating_Original()
    {
        var snapshot = CreateSnapshot(sentimentMarketRegime: MarketRegimes.Neutral);
        var formatter = new SpyFormatter();
        var composer = new AnalyticsOutputComposer(new StubClassifier(MarketRegimes.Volatile), formatter);

        var result = composer.Compose(snapshot);

        result.MarketRegime.Should().Be(MarketRegimes.Volatile);
        result.FormattedContext.Should().Be(MarketRegimes.Volatile);
        formatter.CapturedSnapshot.Should().NotBeNull();
        formatter.CapturedSnapshot!.Sentiment.MarketRegime.Should().Be(MarketRegimes.Volatile);
        snapshot.Sentiment.MarketRegime.Should().Be(MarketRegimes.Neutral);
    }

    private static MarketAnalysisSnapshot CreateSnapshot(string sentimentMarketRegime = MarketRegimes.Neutral) =>
        new()
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "linear",
            CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero),
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
                OpenInterestChange4hPct = 3m,
            },
            OrderBook = new OrderBookSnapshot
            {
                CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero),
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
                WindowStartUtc = new DateTimeOffset(2026, 4, 12, 11, 45, 0, TimeSpan.Zero),
                WindowEndUtc = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero),
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
            H1 = CreateTimeframe("1h"),
            H4 = CreateTimeframe("4h"),
            D1 = CreateTimeframe("1d"),
            Sentiment = new SentimentSnapshot
            {
                LongShortBiasScore = 0.1m,
                FundingBiasScore = 0m,
                OrderBookPressureScore = 0.05m,
                TradeFlowPressureScore = 0.04m,
                MarketRegime = sentimentMarketRegime,
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
            LastCandleOpenTimeUtc = new DateTimeOffset(2026, 4, 12, 11, 0, 0, TimeSpan.Zero),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(2026, 4, 12, 11, 0, 0, TimeSpan.Zero),
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
            Rsi14IsReliable = true,
            Atr14 = 180m,
            VolumeSma20 = 1000m,
            VolumeRatio = 1.1m,
            TrendStrengthScore = 0.4m,
            Trend = MarketTrend.Unknown,
            Support1 = 64600m,
            Support2 = 64250m,
            Resistance1 = 65200m,
            Resistance2 = 65650m,
            CandleRangePct = 0.5385m,
            DistanceToSupport1Pct = 0.6154m,
            DistanceToResistance1Pct = 0.3077m,
        };

    private sealed class StubClassifier(string marketRegime) : IMarketRegimeClassifier
    {
        public string Classify(MarketAnalysisSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            return marketRegime;
        }
    }

    private sealed class StubFormatter(string result = "formatted") : IAnalyticsFormatter
    {
        public MarketAnalysisSnapshot? CapturedSnapshot { get; private set; }

        public string Format(MarketAnalysisSnapshot snapshot)
        {
            CapturedSnapshot = snapshot;
            return result;
        }
    }

    private sealed class SpyFormatter : IAnalyticsFormatter
    {
        public MarketAnalysisSnapshot? CapturedSnapshot { get; private set; }

        public string Format(MarketAnalysisSnapshot snapshot)
        {
            CapturedSnapshot = snapshot;
            return snapshot.Sentiment.MarketRegime;
        }
    }
}
