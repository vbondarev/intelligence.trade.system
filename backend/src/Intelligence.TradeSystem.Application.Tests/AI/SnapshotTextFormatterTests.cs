using System.Globalization;
using Intelligence.TradeSystem.Application.AI;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Tests.AI;

public sealed class SnapshotTextFormatterTests
{
    private readonly SnapshotTextFormatter _formatter = new();

    [Fact]
    public void Throws_When_Snapshot_Is_Null()
    {
        var action = () => _formatter.Format(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("snapshot");
    }

    [Fact]
    public void Includes_All_Expected_Sections_In_Fixed_Order()
    {
        var snapshot = CreateSnapshot();

        var result = _formatter.Format(snapshot);

        var snapshotIndex = result.IndexOf("snapshot:", StringComparison.Ordinal);
        var priceIndex = result.IndexOf("price:", StringComparison.Ordinal);
        var derivativesIndex = result.IndexOf("derivatives:", StringComparison.Ordinal);
        var orderBookIndex = result.IndexOf("order_book:", StringComparison.Ordinal);
        var tradeFlowIndex = result.IndexOf("trade_flow:", StringComparison.Ordinal);
        var trendIndex = result.IndexOf("trend:", StringComparison.Ordinal);
        var sentimentIndex = result.IndexOf("sentiment:", StringComparison.Ordinal);
        var portfolioIndex = result.IndexOf("portfolio:", StringComparison.Ordinal);

        snapshotIndex.Should().BeGreaterThanOrEqualTo(0);
        priceIndex.Should().BeGreaterThan(snapshotIndex);
        derivativesIndex.Should().BeGreaterThan(priceIndex);
        orderBookIndex.Should().BeGreaterThan(derivativesIndex);
        tradeFlowIndex.Should().BeGreaterThan(orderBookIndex);
        trendIndex.Should().BeGreaterThan(tradeFlowIndex);
        sentimentIndex.Should().BeGreaterThan(trendIndex);
        portfolioIndex.Should().BeGreaterThan(sentimentIndex);
    }

    [Fact]
    public void Renders_Key_Collections_And_Tag_List_Using_Stable_Compact_Format()
    {
        var snapshot = CreateSnapshot(
            tags: ["trend-following", "", "high-conviction"],
            bidWalls:
            [
                new LiquidityWall { Price = 64850m, Size = 1200m, DistancePctFromMarket = 0.75m },
                new LiquidityWall { Price = 64700m, Size = 950m, DistancePctFromMarket = 1.23m },
            ],
            askWalls:
            [
                new LiquidityWall { Price = 65150m, Size = 1400m, DistancePctFromMarket = 0.65m },
            ],
            openPositions:
            [
                new OpenPositionSnapshot
                {
                    Symbol = "BTCUSDT",
                    Side = PositionSide.Long,
                    Size = 0.75m,
                    AvgPrice = 64000m,
                    MarkPrice = 65000m,
                    BreakEvenPrice = 64120m,
                    LiquidationPrice = 61000m,
                    PositionValueUsd = 48750m,
                    Leverage = 5m,
                    UnrealizedPnlUsd = 750m,
                    UnrealizedPnlPct = 1.5385m,
                },
            ]);

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  tags: [trend-following, high-conviction]");
        result.Should().Contain("  bid_walls: price=64850, size=1200, distance_pct=0.75%; price=64700, size=950, distance_pct=1.23%", Exactly.Once());
        result.Should().Contain("  ask_walls: price=65150, size=1400, distance_pct=0.65%", Exactly.Once());
        result.Should().Contain("  open_positions: symbol=BTCUSDT, side=Long, size=0.75, avg_price=64000, mark_price=65000, break_even_price=64120, liquidation_price=61000, position_value_usd=48750, leverage=5, unrealized_pnl_usd=750, unrealized_pnl_pct=1.5385%", Exactly.Once());
    }

    [Fact]
    public void Uses_Placeholders_For_Null_And_Empty_Values()
    {
        var snapshot = CreateSnapshot(
            tags: [],
            sentimentMarketRegime: " ",
            nextFundingTimeUtc: null,
            premiumVsIndexPct: null,
            bidWalls: [],
            askWalls: [],
            openPositions: []);

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  tags: []");
        result.Should().Contain("  next_funding_time_utc: n/a");
        result.Should().Contain("  premium_vs_index_pct: n/a");
        result.Should().Contain("  bid_walls: none");
        result.Should().Contain("  ask_walls: none");
        result.Should().Contain("  market_regime: n/a");
        result.Should().Contain("  open_positions: none");
    }

    [Fact]
    public void Renders_All_Whitespace_Tags_As_Empty_Array()
    {
        var snapshot = CreateSnapshot(tags: [" ", "\t", string.Empty]);

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  tags: []");
    }

    [Fact]
    public void Uses_Invariant_Culture_For_Decimals_And_Dates()
    {
        var snapshot = CreateSnapshot(
            capturedAtUtc: new DateTimeOffset(2026, 4, 12, 13, 45, 56, TimeSpan.Zero),
            lastPrice: 65000.5m,
            spreadPct: 1.25m,
            nextFundingTimeUtc: new DateTimeOffset(2026, 4, 12, 16, 0, 0, TimeSpan.Zero));

        using var _ = new CultureScope("ru-RU");

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  captured_at_utc: 2026-04-12T13:45:56.0000000+00:00");
        result.Should().Contain("  last_price: 65000.5");
        result.Should().Contain("  spread_pct: 1.25%");
        result.Should().Contain("  next_funding_time_utc: 2026-04-12T16:00:00.0000000+00:00");
        result.Should().NotContain("65000,5");
        result.Should().NotContain("1,25%");
    }

    [Fact]
    public void Preserves_Price_Like_Precision_For_Low_Priced_Instruments()
    {
        var snapshot = CreateSnapshot(
            symbol: "SHIBUSDT",
            lastPrice: 0.00003452m,
            markPrice: 0.00003449m,
            indexPrice: 0.00003444m,
            bidPrice: 0.00003450m,
            askPrice: 0.00003454m,
            high24h: 0.00003510m,
            low24h: 0.00003280m,
            bidWalls:
            [
                new LiquidityWall { Price = 0.00003410m, Size = 12500000m, DistancePctFromMarket = 1.22m },
            ],
            openPositions:
            [
                new OpenPositionSnapshot
                {
                    Symbol = "SHIBUSDT",
                    Side = PositionSide.Long,
                    Size = 1500000m,
                    AvgPrice = 0.00003390m,
                    MarkPrice = 0.00003452m,
                    BreakEvenPrice = 0.00003401m,
                    LiquidationPrice = 0.00002980m,
                    PositionValueUsd = 51.78m,
                    Leverage = 3m,
                    UnrealizedPnlUsd = 0.93m,
                    UnrealizedPnlPct = 1.795m,
                },
            ],
            m15: CreateTimeframe("15m") with
            {
                LastCandle = CreateCandle(close: 0.00003452m),
                Ema20 = 0.00003410m,
                Ema50 = 0.00003380m,
                Ema200 = 0.00003150m,
                Atr14 = 0.00000085m,
                Support1 = 0.00003390m,
                Support2 = 0.00003310m,
                Resistance1 = 0.00003490m,
                Resistance2 = 0.00003560m,
            });

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  last_price: 0.00003452");
        result.Should().Contain("  mark_price: 0.00003449");
        result.Should().Contain("  bid_price: 0.0000345");
        result.Should().Contain("  ask_price: 0.00003454");
        result.Should().Contain("  high_24h: 0.0000351");
        result.Should().Contain("  low_24h: 0.0000328");
        result.Should().Contain("price=0.0000341, size=12500000, distance_pct=1.22%");
        result.Should().Contain("avg_price=0.0000339, mark_price=0.00003452, break_even_price=0.00003401, liquidation_price=0.0000298");
        result.Should().Contain("  15m: trend=Unknown, strength=0.4, rsi14=55, atr14=0.00000085");
        result.Should().Contain(", ema20=0.0000341, ema50=0.0000338, ema200=0.0000315");
        result.Should().Contain(", support1=0.0000339, support2=0.0000331, resistance1=0.0000349, resistance2=0.0000356");
        result.Should().NotContain("last_price: 0\r\n");
        result.Should().NotContain("mark_price: 0\r\n");
    }

    [Fact]
    public void Keeps_Btc_Like_Prices_Compact_While_Using_Higher_Precision_For_Price_Like_Fields_Only()
    {
        var snapshot = CreateSnapshot();

        var result = _formatter.Format(snapshot);

        result.Should().Contain("  last_price: 65000");
        result.Should().Contain("  mark_price: 64990.25");
        result.Should().Contain("  bid_size: 10.5");
        result.Should().Contain("  long_ratio: 0.52");
        result.Should().Contain("  total_equity_usd: 10000");
        result.Should().Contain("  spread_pct: 0.0154%");
    }

    [Fact]
    public void Returns_Deterministic_Output_For_Same_Input()
    {
        var snapshot = CreateSnapshot();

        var first = _formatter.Format(snapshot);
        var second = _formatter.Format(snapshot);

        second.Should().Be(first);
    }

    [Fact]
    public void Renders_Timeframes_In_Fixed_Order_M15_H1_H4_D1()
    {
        var snapshot = CreateSnapshot();

        var result = _formatter.Format(snapshot);

        var m15Index = result.IndexOf("  15m:", StringComparison.Ordinal);
        var h1Index = result.IndexOf("  1h:", StringComparison.Ordinal);
        var h4Index = result.IndexOf("  4h:", StringComparison.Ordinal);
        var d1Index = result.IndexOf("  1d:", StringComparison.Ordinal);

        m15Index.Should().BeGreaterThanOrEqualTo(0);
        h1Index.Should().BeGreaterThan(m15Index);
        h4Index.Should().BeGreaterThan(h1Index);
        d1Index.Should().BeGreaterThan(h4Index);
    }

    private static MarketAnalysisSnapshot CreateSnapshot(
        List<string>? tags = null,
        string sentimentMarketRegime = MarketRegimes.Trending,
        DateTimeOffset? capturedAtUtc = null,
        DateTimeOffset? nextFundingTimeUtc = null,
        decimal? premiumVsIndexPct = 0.18m,
        string symbol = "BTCUSDT",
        decimal lastPrice = 65000m,
        decimal markPrice = 64990.25m,
        decimal indexPrice = 64980.5m,
        decimal bidPrice = 64995m,
        decimal askPrice = 65005m,
        decimal high24h = 65200m,
        decimal low24h = 64000m,
        decimal spreadPct = 0.0154m,
        List<LiquidityWall>? bidWalls = null,
        List<LiquidityWall>? askWalls = null,
        List<OpenPositionSnapshot>? openPositions = null,
        TimeframeAnalysisSnapshot? m15 = null) =>
        new()
        {
            Exchange = "Bybit",
            Symbol = symbol,
            Category = "linear",
            CapturedAtUtc = capturedAtUtc ?? new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.Zero),
            Price = new PriceSnapshot
            {
                LastPrice = lastPrice,
                MarkPrice = markPrice,
                IndexPrice = indexPrice,
                BidPrice = bidPrice,
                AskPrice = askPrice,
                BidSize = 10.5m,
                AskSize = 12.25m,
                SpreadAbs = 10m,
                SpreadPct = spreadPct,
                Price24hChangePct = 1.2m,
                High24h = high24h,
                Low24h = low24h,
                Volume24h = 12345.67m,
                Turnover24h = 800000000.12m,
            },
            Derivatives = new DerivativesSnapshot
            {
                FundingRate = 0.0001m,
                FundingRateAvg24h = 0.0002m,
                NextFundingTimeUtc = nextFundingTimeUtc,
                OpenInterest = 100000m,
                OpenInterestValue = 6500000000m,
                LongRatio = 0.52m,
                ShortRatio = 0.48m,
                PremiumVsIndexPct = premiumVsIndexPct,
                OpenInterestChange1hPct = 1.5m,
                OpenInterestChange4hPct = 3m,
            },
            OrderBook = new OrderBookSnapshot
            {
                CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 12, 0, 10, TimeSpan.Zero),
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
                BidWalls = bidWalls ?? [],
                AskWalls = askWalls ?? [],
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
                HasAggressiveBuyPressure = true,
                HasAggressiveSellPressure = false,
            },
            M15 = m15 ?? CreateTimeframe("15m"),
            H1 = CreateTimeframe("1h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.78m },
            H4 = CreateTimeframe("4h") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.72m },
            D1 = CreateTimeframe("1d") with { Trend = MarketTrend.Bullish, TrendStrengthScore = 0.68m },
            Sentiment = new SentimentSnapshot
            {
                LongShortBiasScore = 0.1m,
                FundingBiasScore = -0.02m,
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
                OpenPositions = openPositions ?? [],
            },
            Tags = tags ?? ["trend", "momentum"],
        };

    private static CandleSnapshot CreateCandle(decimal close) =>
        new()
        {
            OpenTimeUtc = new DateTimeOffset(2026, 4, 12, 11, 0, 0, TimeSpan.Zero),
            Open = close,
            High = close,
            Low = close,
            Close = close,
            Volume = 1200m,
            Turnover = 78000000m,
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
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = true,
            EmaBullishAlignment = true,
            CandleRangePct = 0.5385m,
            DistanceToSupport1Pct = 0.6154m,
            DistanceToResistance1Pct = 0.3077m,
        };

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture;
        private readonly CultureInfo _originalUiCulture;

        public CultureScope(string cultureName)
        {
            _originalCulture = CultureInfo.CurrentCulture;
            _originalUiCulture = CultureInfo.CurrentUICulture;
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
        }
    }
}
