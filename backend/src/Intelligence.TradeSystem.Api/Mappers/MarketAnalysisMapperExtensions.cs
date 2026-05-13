using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Models.MarketAnalysis;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class MarketAnalysisMapperExtensions
{
    public static MarketAnalysisResponse ToResponse(this MarketAnalysisSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new MarketAnalysisResponse
        {
            Exchange = snapshot.Exchange,
            Symbol = snapshot.Symbol,
            Category = snapshot.Category,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            Price = ToPriceModel(snapshot.Price),
            Derivatives = ToDerivativesModel(snapshot.Derivatives),
            OrderBook = ToOrderBookModel(snapshot.OrderBook),
            TradeFlow = ToTradeFlowModel(snapshot.TradeFlow),
            M15 = ToTimeframeModel(snapshot.M15),
            H1 = ToTimeframeModel(snapshot.H1),
            H4 = ToTimeframeModel(snapshot.H4),
            D1 = ToTimeframeModel(snapshot.D1),
            Sentiment = ToSentimentModel(snapshot.Sentiment),
            Portfolio = ToPortfolioModel(snapshot.Portfolio),
            Tags = [.. snapshot.Tags],
        };
    }

    private static PriceModel ToPriceModel(PriceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PriceModel
        {
            LastPrice = snapshot.LastPrice,
            MarkPrice = snapshot.MarkPrice,
            IndexPrice = snapshot.IndexPrice,
            BidPrice = snapshot.BidPrice,
            AskPrice = snapshot.AskPrice,
            BidSize = snapshot.BidSize,
            AskSize = snapshot.AskSize,
            SpreadAbs = snapshot.SpreadAbs,
            SpreadPct = snapshot.SpreadPct,
            Price24hChangePct = snapshot.Price24hChangePct,
            High24h = snapshot.High24h,
            Low24h = snapshot.Low24h,
            Volume24h = snapshot.Volume24h,
            Turnover24h = snapshot.Turnover24h,
        };
    }

    private static DerivativesModel ToDerivativesModel(DerivativesSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DerivativesModel
        {
            FundingRate = snapshot.FundingRate,
            NextFundingTimeUtc = snapshot.NextFundingTimeUtc,
            OpenInterest = snapshot.OpenInterest,
            OpenInterestValue = snapshot.OpenInterestValue,
            LongRatio = snapshot.LongRatio,
            ShortRatio = snapshot.ShortRatio,
            PremiumVsIndexPct = snapshot.PremiumVsIndexPct,
            OpenInterestChange1hPct = snapshot.OpenInterestChange1hPct,
            OpenInterestChange4hPct = snapshot.OpenInterestChange4hPct,
            FundingRateAvg24h = snapshot.FundingRateAvg24h,
        };
    }

    private static OrderBookModel ToOrderBookModel(OrderBookSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new OrderBookModel
        {
            CapturedAtUtc = snapshot.CapturedAtUtc,
            BestBidPrice = snapshot.BestBidPrice,
            BestAskPrice = snapshot.BestAskPrice,
            TotalBidVolumeTop5 = snapshot.TotalBidVolumeTop5,
            TotalAskVolumeTop5 = snapshot.TotalAskVolumeTop5,
            TotalBidVolumeTop10 = snapshot.TotalBidVolumeTop10,
            TotalAskVolumeTop10 = snapshot.TotalAskVolumeTop10,
            TotalBidVolumeTop20 = snapshot.TotalBidVolumeTop20,
            TotalAskVolumeTop20 = snapshot.TotalAskVolumeTop20,
            ImbalanceTop5 = snapshot.ImbalanceTop5,
            ImbalanceTop10 = snapshot.ImbalanceTop10,
            ImbalanceTop20 = snapshot.ImbalanceTop20,
            TopBids = [.. snapshot.TopBids.Select(ToOrderBookLevelModel)],
            TopAsks = [.. snapshot.TopAsks.Select(ToOrderBookLevelModel)],
            BidWalls = [.. snapshot.BidWalls.Select(ToLiquidityWallModel)],
            AskWalls = [.. snapshot.AskWalls.Select(ToLiquidityWallModel)],
        };
    }

    private static TradeFlowModel ToTradeFlowModel(TradeFlowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TradeFlowModel
        {
            WindowStartUtc = snapshot.WindowStartUtc,
            WindowEndUtc = snapshot.WindowEndUtc,
            BuyVolume = snapshot.BuyVolume,
            SellVolume = snapshot.SellVolume,
            DeltaVolume = snapshot.DeltaVolume,
            DeltaPct = snapshot.DeltaPct,
            TotalTrades = snapshot.TotalTrades,
            BuyTrades = snapshot.BuyTrades,
            SellTrades = snapshot.SellTrades,
            AvgTradeSize = snapshot.AvgTradeSize,
            MaxTradeSize = snapshot.MaxTradeSize,
            HasAggressiveBuyPressure = snapshot.HasAggressiveBuyPressure,
            HasAggressiveSellPressure = snapshot.HasAggressiveSellPressure,
        };
    }

    private static TimeframeModel ToTimeframeModel(TimeframeAnalysisSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new TimeframeModel
        {
            Timeframe = snapshot.Timeframe,
            LastCandleOpenTimeUtc = snapshot.LastCandleOpenTimeUtc,
            LastCandle = ToCandleModel(snapshot.LastCandle),
            Ema20 = snapshot.Ema20,
            Ema50 = snapshot.Ema50,
            Ema200 = snapshot.Ema200,
            Rsi14 = snapshot.Rsi14,
            Rsi14IsReliable = snapshot.Rsi14IsReliable,
            Atr14 = snapshot.Atr14,
            VolumeSma20 = snapshot.VolumeSma20,
            VolumeRatio = snapshot.VolumeRatio,
            TrendStrengthScore = snapshot.TrendStrengthScore,
            Trend = snapshot.Trend.ToString(),
            Support1 = snapshot.Support1,
            Support2 = snapshot.Support2,
            Resistance1 = snapshot.Resistance1,
            Resistance2 = snapshot.Resistance2,
            IsAboveEma20 = snapshot.IsAboveEma20,
            IsAboveEma50 = snapshot.IsAboveEma50,
            IsAboveEma200 = snapshot.IsAboveEma200,
            EmaBullishAlignment = snapshot.EmaBullishAlignment,
            EmaBearishAlignment = snapshot.EmaBearishAlignment,
            RsiOverbought = snapshot.RsiOverbought,
            RsiOversold = snapshot.RsiOversold,
            CandleRangePct = snapshot.CandleRangePct,
            DistanceToSupport1Pct = snapshot.DistanceToSupport1Pct,
            DistanceToResistance1Pct = snapshot.DistanceToResistance1Pct,
        };
    }

    private static SentimentModel ToSentimentModel(SentimentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new SentimentModel
        {
            LongShortBiasScore = snapshot.LongShortBiasScore,
            FundingBiasScore = snapshot.FundingBiasScore,
            OrderBookPressureScore = snapshot.OrderBookPressureScore,
            TradeFlowPressureScore = snapshot.TradeFlowPressureScore,
            MarketRegime = snapshot.MarketRegime,
        };
    }

    private static PortfolioModel ToPortfolioModel(PortfolioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new PortfolioModel
        {
            TotalEquityUsd = snapshot.TotalEquityUsd,
            AvailableBalanceUsd = snapshot.AvailableBalanceUsd,
            TotalWalletBalanceUsd = snapshot.TotalWalletBalanceUsd,
            TotalUnrealizedPnlUsd = snapshot.TotalUnrealizedPnlUsd,
            OpenPositions = [.. snapshot.OpenPositions.Select(ToOpenPositionModel)],
        };
    }

    private static OrderBookLevelModel ToOrderBookLevelModel(OrderBookLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);

        return new OrderBookLevelModel
        {
            Price = level.Price,
            Size = level.Size,
        };
    }

    private static LiquidityWallModel ToLiquidityWallModel(LiquidityWall wall)
    {
        ArgumentNullException.ThrowIfNull(wall);

        return new LiquidityWallModel
        {
            Price = wall.Price,
            Size = wall.Size,
            DistancePctFromMarket = wall.DistancePctFromMarket,
        };
    }

    private static CandleModel ToCandleModel(CandleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new CandleModel
        {
            OpenTimeUtc = snapshot.OpenTimeUtc,
            Open = snapshot.Open,
            High = snapshot.High,
            Low = snapshot.Low,
            Close = snapshot.Close,
            Volume = snapshot.Volume,
            Turnover = snapshot.Turnover,
        };
    }

    private static OpenPositionModel ToOpenPositionModel(OpenPositionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new OpenPositionModel
        {
            Symbol = snapshot.Symbol,
            Side = snapshot.Side.ToString(),
            Size = snapshot.Size,
            AvgPrice = snapshot.AvgPrice,
            MarkPrice = snapshot.MarkPrice,
            BreakEvenPrice = snapshot.BreakEvenPrice,
            LiquidationPrice = snapshot.LiquidationPrice,
            PositionValueUsd = snapshot.PositionValueUsd,
            Leverage = snapshot.Leverage,
            UnrealizedPnlUsd = snapshot.UnrealizedPnlUsd,
            UnrealizedPnlPct = snapshot.UnrealizedPnlPct,
        };
    }
}
