using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions.Market;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;
using Intelligence.TradeSystem.Api.Serialization;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionMarketMapper
{
    public static PositionMarketResponse ToResponse(PositionMarketContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Snapshot);

        var identity = context.Identity;
        var snapshot = context.Snapshot;
        return new PositionMarketResponse(
            identity.PositionId.Value,
            ToWireExchange(identity.ExchangeId),
            identity.Symbol,
            ToWireMarketCategory(identity.MarketCategory),
            snapshot.CapturedAtUtc,
            ToPriceResponse(snapshot.Price),
            ToDerivativesResponse(snapshot.Derivatives),
            ToOrderBookResponse(snapshot.OrderBook),
            ToTradeFlowResponse(snapshot.TradeFlow),
            ToTimeframeResponse(snapshot.M15),
            ToTimeframeResponse(snapshot.H1),
            ToTimeframeResponse(snapshot.H4),
            ToTimeframeResponse(snapshot.D1),
            ToSentimentResponse(snapshot.Sentiment),
            [.. snapshot.Tags]);
    }

    public static PositionCandlesResponse ToResponse(PositionCandles candles)
    {
        ArgumentNullException.ThrowIfNull(candles);

        var identity = candles.Identity;
        return new PositionCandlesResponse(
            identity.PositionId.Value,
            ToWireExchange(identity.ExchangeId),
            identity.Symbol,
            ToWireMarketCategory(identity.MarketCategory),
            CandleIntervalV1Codec.ToWireValue(candles.Interval),
            [.. candles.Items.Select(ToCandleResponse)]);
    }

    private static PositionMarketPriceResponse ToPriceResponse(PriceSnapshot snapshot) => new(
        snapshot.LastPrice,
        snapshot.MarkPrice,
        snapshot.IndexPrice,
        snapshot.BidPrice,
        snapshot.AskPrice,
        snapshot.SpreadAbs,
        snapshot.SpreadPct,
        snapshot.Price24hChangePct,
        snapshot.High24h,
        snapshot.Low24h,
        snapshot.Volume24h,
        snapshot.Turnover24h);

    private static PositionMarketDerivativesResponse ToDerivativesResponse(
        DerivativesSnapshot snapshot) => new(
        snapshot.FundingRate,
        snapshot.NextFundingTimeUtc,
        snapshot.OpenInterest,
        snapshot.OpenInterestValue,
        snapshot.OpenInterestChange1hPct,
        snapshot.OpenInterestChange4hPct,
        snapshot.LongRatio,
        snapshot.ShortRatio,
        snapshot.PremiumVsIndexPct);

    private static PositionMarketOrderBookResponse ToOrderBookResponse(
        OrderBookSnapshot snapshot) => new(
        snapshot.CapturedAtUtc,
        snapshot.BestBidPrice,
        snapshot.BestAskPrice,
        snapshot.ImbalanceTop5,
        snapshot.ImbalanceTop10,
        snapshot.ImbalanceTop20);

    private static PositionMarketTradeFlowResponse ToTradeFlowResponse(
        TradeFlowSnapshot snapshot) => new(
        snapshot.WindowStartUtc,
        snapshot.WindowEndUtc,
        snapshot.BuyVolume,
        snapshot.SellVolume,
        snapshot.DeltaVolume,
        snapshot.DeltaPct,
        snapshot.TotalTrades,
        snapshot.HasAggressiveBuyPressure,
        snapshot.HasAggressiveSellPressure);

    private static PositionMarketTimeframeResponse ToTimeframeResponse(
        TimeframeAnalysisSnapshot snapshot) => new(
        ToCandleResponse(snapshot.LastCandle),
        snapshot.Ema20,
        snapshot.Ema50,
        snapshot.Ema200,
        snapshot.Rsi14,
        snapshot.Atr14,
        snapshot.VolumeRatio,
        ToWireTrend(snapshot.Trend),
        snapshot.TrendStrengthScore,
        snapshot.Support1,
        snapshot.Support2,
        snapshot.Resistance1,
        snapshot.Resistance2,
        snapshot.DistanceToSupport1Pct,
        snapshot.DistanceToResistance1Pct,
        snapshot.Rsi14IsReliable,
        snapshot.EmaIsReliable,
        snapshot.EmaHasFallback,
        snapshot.AtrIsReliable,
        snapshot.AtrIsFallback,
        snapshot.VolumeRatioIsReliable,
        snapshot.VolumeRatioIsFallback);

    private static PositionMarketSentimentResponse ToSentimentResponse(
        SentimentSnapshot snapshot) => new(
        ToWireMarketRegime(snapshot.MarketRegime),
        snapshot.LongShortBiasScore,
        snapshot.FundingBiasScore,
        snapshot.OrderBookPressureScore,
        snapshot.TradeFlowPressureScore);

    private static PositionMarketCandleResponse ToCandleResponse(CandleSnapshot candle) => new(
        candle.OpenTimeUtc,
        candle.Open,
        candle.High,
        candle.Low,
        candle.Close,
        candle.Volume,
        candle.Turnover);

    private static PositionMarketCandleResponse ToCandleResponse(Kline kline) => new(
        new DateTimeOffset(DateTime.SpecifyKind(kline.StartTime, DateTimeKind.Utc)),
        kline.Open,
        kline.High,
        kline.Low,
        kline.Close,
        kline.Volume,
        kline.Turnover);

    private static ExchangeProvider ToWireExchange(ExchangeId exchangeId) => exchangeId switch
    {
        ExchangeId.Bybit => ExchangeProvider.Bybit,
        _ => throw new NotSupportedException(
            $"Exchange '{exchangeId}' is not mapped to a v1 wire contract."),
    };

    private static MarketCategoryV1 ToWireMarketCategory(MarketCategory category) => category switch
    {
        MarketCategory.Linear => MarketCategoryV1.Linear,
        MarketCategory.Inverse => MarketCategoryV1.Inverse,
        _ => throw new NotSupportedException(
            $"Market category '{category}' is not mapped to a v1 wire contract."),
    };

    private static MarketTrendV1 ToWireTrend(MarketTrend trend) => trend switch
    {
        MarketTrend.Unknown => MarketTrendV1.Unknown,
        MarketTrend.Bullish => MarketTrendV1.Bullish,
        MarketTrend.Bearish => MarketTrendV1.Bearish,
        MarketTrend.Sideways => MarketTrendV1.Sideways,
        _ => throw new NotSupportedException(
            $"Market trend '{trend}' is not mapped to a v1 wire contract."),
    };

    private static MarketRegimeV1 ToWireMarketRegime(string regime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regime);

        return regime switch
        {
            MarketRegimes.Trending => MarketRegimeV1.Trending,
            MarketRegimes.Volatile => MarketRegimeV1.Volatile,
            MarketRegimes.MeanReversion => MarketRegimeV1.MeanReversion,
            MarketRegimes.Neutral => MarketRegimeV1.Neutral,
            _ => throw new NotSupportedException(
                $"Market regime '{regime}' is not mapped to a v1 wire contract."),
        };
    }
}
