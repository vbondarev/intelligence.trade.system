using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal static class ApiSnapshotTestData
{
    public static MarketAnalysisSnapshot CreateSnapshot() => CreateSnapshot(MarketTrend.Bullish);

    /// <summary>
    /// Создаёт снапшот, в котором все таймфреймы имеют указанный тренд.
    /// </summary>
    public static MarketAnalysisSnapshot CreateSnapshot(MarketTrend trend) =>
        CreateSnapshot(trend, overrideIsAboveEma200: null, overrideEmaBullish: null, overrideEmaBearish: null);

    /// <summary>
    /// Создаёт снапшот с явными override'ами EMA-флагов для всех таймфреймов.
    /// Используется для тестирования граничных случаев <c>isTrendConfirmed</c>.
    /// </summary>
    public static MarketAnalysisSnapshot CreateSnapshot(
        MarketTrend trend,
        bool? overrideIsAboveEma200,
        bool? overrideEmaBullish,
        bool? overrideEmaBearish,
        decimal? overrideRsi14 = null,
        bool? overrideRsiOverbought = null,
        bool? overrideRsiOversold = null) =>
        new()
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "linear",
            CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 14, 0, 0, TimeSpan.Zero),
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
                FundingRateAvg24h = 0.0002m,
                NextFundingTimeUtc = new DateTimeOffset(2026, 4, 12, 16, 0, 0, TimeSpan.Zero),
                OpenInterest = 100000m,
                OpenInterestValue = 6500000000m,
                LongRatio = 0.52m,
                ShortRatio = 0.48m,
                PremiumVsIndexPct = 0.0154m,
                OpenInterestChange1hPct = 1.5m,
                OpenInterestChange4hPct = 3m,
            },
            OrderBook = new OrderBookSnapshot
            {
                CapturedAtUtc = new DateTimeOffset(2026, 4, 12, 14, 0, 0, TimeSpan.Zero),
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
                TopBids =
                [
                    new OrderBookLevel { Price = 64995m, Size = 10m },
                    new OrderBookLevel { Price = 64990m, Size = 9m },
                ],
                TopAsks =
                [
                    new OrderBookLevel { Price = 65005m, Size = 12m },
                    new OrderBookLevel { Price = 65010m, Size = 11m },
                ],
                BidWalls =
                [
                    new LiquidityWall { Price = 64850m, Size = 50m, DistancePctFromMarket = 0.23m },
                ],
                AskWalls =
                [
                    new LiquidityWall { Price = 65150m, Size = 45m, DistancePctFromMarket = 0.23m },
                ],
            },
            TradeFlow = new TradeFlowSnapshot
            {
                WindowStartUtc = new DateTimeOffset(2026, 4, 12, 13, 45, 0, TimeSpan.Zero),
                WindowEndUtc = new DateTimeOffset(2026, 4, 12, 14, 0, 0, TimeSpan.Zero),
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
            M15 = CreateTimeframe("15m", trend, overrideIsAboveEma200, overrideEmaBullish, overrideEmaBearish, overrideRsi14, overrideRsiOverbought, overrideRsiOversold),
            H1 = CreateTimeframe("1h", trend, overrideIsAboveEma200, overrideEmaBullish, overrideEmaBearish, overrideRsi14, overrideRsiOverbought, overrideRsiOversold),
            H4 = CreateTimeframe("4h", trend, overrideIsAboveEma200, overrideEmaBullish, overrideEmaBearish, overrideRsi14, overrideRsiOverbought, overrideRsiOversold),
            D1 = CreateTimeframe("1d", trend, overrideIsAboveEma200, overrideEmaBullish, overrideEmaBearish, overrideRsi14, overrideRsiOverbought, overrideRsiOversold),
            Sentiment = new SentimentSnapshot
            {
                LongShortBiasScore = 0.1m,
                FundingBiasScore = -0.02m,
                OrderBookPressureScore = 0.05m,
                TradeFlowPressureScore = 0.04m,
                MarketRegime = "Trending",
            },
            Portfolio = new PortfolioSnapshot
            {
                TotalEquityUsd = 10000m,
                AvailableBalanceUsd = 8000m,
                TotalWalletBalanceUsd = 9500m,
                TotalUnrealizedPnlUsd = 500m,
                OpenPositions =
                [
                    new OpenPositionSnapshot
                    {
                        Symbol = "BTCUSDT",
                        Side = PositionSide.Long,
                        Size = 0.5m,
                        AvgPrice = 64000m,
                        MarkPrice = 65000m,
                        BreakEvenPrice = 64100m,
                        LiquidationPrice = 58000m,
                        PositionValueUsd = 32500m,
                        Leverage = 5m,
                        UnrealizedPnlUsd = 500m,
                        UnrealizedPnlPct = 1.56m,
                    },
                ],
            },
            Tags = ["trend", "momentum"],
        };

    private static TimeframeAnalysisSnapshot CreateTimeframe(
        string timeframe,
        MarketTrend trend = MarketTrend.Bullish,
        bool? overrideIsAboveEma200 = null,
        bool? overrideEmaBullish = null,
        bool? overrideEmaBearish = null,
        decimal? overrideRsi14 = 55m,
        bool? overrideRsiOverbought = null,
        bool? overrideRsiOversold = null)
    {
        // Семантически корректные дефолты:
        // Bearish-тренд → цена ниже EMA200; Bullish/Sideways/Unknown → выше.
        var isAboveEma200  = overrideIsAboveEma200 ?? (trend != MarketTrend.Bearish);
        var emaBullish     = overrideEmaBullish     ?? (trend == MarketTrend.Bullish);
        var emaBearish     = overrideEmaBearish     ?? (trend == MarketTrend.Bearish);
        // overrideRsi14 = null explicitly means "RSI unavailable"; default 55m = available.
        decimal? rsi14     = overrideRsi14;
        var rsiOverbought  = overrideRsiOverbought  ?? false;
        var rsiOversold    = overrideRsiOversold    ?? false;

        return new TimeframeAnalysisSnapshot
        {
            Timeframe = timeframe,
            LastCandleOpenTimeUtc = new DateTimeOffset(2026, 4, 12, 13, 0, 0, TimeSpan.Zero),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = new DateTimeOffset(2026, 4, 12, 13, 0, 0, TimeSpan.Zero),
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
            Rsi14 = rsi14,
            Rsi14IsReliable = rsi14.HasValue,
            Atr14 = 180m,
            VolumeSma20 = 1000m,
            VolumeRatio = 1.1m,
            TrendStrengthScore = 0.4m,
            Trend = trend,
            Support1 = 64600m,
            Support2 = 64250m,
            Resistance1 = 65200m,
            Resistance2 = 65650m,
            IsAboveEma20 = true,
            IsAboveEma50 = true,
            IsAboveEma200 = isAboveEma200,
            EmaBullishAlignment = emaBullish,
            EmaBearishAlignment = emaBearish,
            RsiOverbought = rsiOverbought,
            RsiOversold = rsiOversold,
            EmaIsReliable         = true,
            EmaHasFallback        = false,
            AtrIsReliable         = true,
            AtrIsFallback         = false,
            VolumeRatioIsReliable  = true,
            VolumeRatioIsFallback  = false,
            CandleRangePct = 0.5385m,
            DistanceToSupport1Pct = 0.6154m,
            DistanceToResistance1Pct = 0.3077m,
        };
    }
}
