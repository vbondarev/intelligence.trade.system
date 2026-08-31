
namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;

/// <summary>
/// Создаёт минимальные non-timeframe domain-снапшоты для integration-тестов,
/// которым нужно собрать <see cref="MarketAnalysisSnapshot"/> без реального API.
/// </summary>
internal static class TestSnapshotFactory
{
    private static readonly DateTimeOffset _fixedNow =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static PriceSnapshot CreatePrice() => new()
    {
        LastPrice = 65_000m,
        MarkPrice = 64_990m,
        IndexPrice = 64_980m,
        BidPrice = 64_995m,
        AskPrice = 65_005m,
        BidSize = 10m,
        AskSize = 12m,
        SpreadAbs = 10m,
        SpreadPct = 0.0154m,
        Price24hChangePct = 1.2m,
        High24h = 65_200m,
        Low24h = 64_000m,
        Volume24h = 12_345m,
        Turnover24h = 800_000_000m,
    };

    public static DerivativesSnapshot CreateDerivatives() => new()
    {
        FundingRate = 0.0001m,
        FundingRateAvg24h = 0.0002m,
        NextFundingTimeUtc = _fixedNow.AddHours(4),
        OpenInterest = 100_000m,
        OpenInterestValue = 6_500_000_000m,
        LongRatio = 0.52m,
        ShortRatio = 0.48m,
        PremiumVsIndexPct = 0.0154m,
        OpenInterestChange1hPct = 1.5m,
        OpenInterestChange4hPct = 3m,
    };

    public static OrderBookSnapshot CreateOrderBook() => new()
    {
        CapturedAtUtc = _fixedNow,
        BestBidPrice = 64_995m,
        BestAskPrice = 65_005m,
        TotalBidVolumeTop5 = 100m,
        TotalAskVolumeTop5 = 95m,
        TotalBidVolumeTop10 = 220m,
        TotalAskVolumeTop10 = 210m,
        TotalBidVolumeTop20 = 420m,
        TotalAskVolumeTop20 = 405m,
        ImbalanceTop5 = 0.02m,
        ImbalanceTop10 = 0.01m,
        ImbalanceTop20 = 0.01m,
        TopBids = [new OrderBookLevel { Price = 64_995m, Size = 10m }],
        TopAsks = [new OrderBookLevel { Price = 65_005m, Size = 12m }],
        BidWalls = [new LiquidityWall { Price = 64_850m, Size = 50m, DistancePctFromMarket = 0.23m }],
        AskWalls = [new LiquidityWall { Price = 65_150m, Size = 45m, DistancePctFromMarket = 0.23m }],
    };

    public static TradeFlowSnapshot CreateTradeFlow() => new()
    {
        WindowStartUtc = _fixedNow.AddMinutes(-15),
        WindowEndUtc = _fixedNow,
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
    };

    public static SentimentSnapshot CreateSentiment() => new()
    {
        LongShortBiasScore = 0.1m,
        FundingBiasScore = -0.02m,
        OrderBookPressureScore = 0.05m,
        TradeFlowPressureScore = 0.04m,
        MarketRegime = "Trending",
    };

    public static PortfolioSnapshot CreatePortfolio() => new()
    {
        TotalEquityUsd = 10_000m,
        AvailableBalanceUsd = 8_000m,
        TotalWalletBalanceUsd = 9_500m,
        TotalUnrealizedPnlUsd = 500m,
        OpenPositions = [],
    };
}
