using Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions.Market;

public sealed record PositionMarketResponse(
    Guid PositionId,
    ExchangeProvider Exchange,
    string Symbol,
    MarketCategoryV1 MarketCategory,
    DateTimeOffset CapturedAt,
    PositionMarketPriceResponse Price,
    PositionMarketDerivativesResponse Derivatives,
    PositionMarketOrderBookResponse OrderBook,
    PositionMarketTradeFlowResponse TradeFlow,
    PositionMarketTimeframeResponse M15,
    PositionMarketTimeframeResponse H1,
    PositionMarketTimeframeResponse H4,
    PositionMarketTimeframeResponse D1,
    PositionMarketSentimentResponse Sentiment,
    IReadOnlyList<string> Tags);

public sealed record PositionMarketPriceResponse(
    decimal LastPrice,
    decimal MarkPrice,
    decimal IndexPrice,
    decimal BidPrice,
    decimal AskPrice,
    decimal SpreadAbs,
    decimal SpreadPct,
    decimal Price24hChangePct,
    decimal High24h,
    decimal Low24h,
    decimal Volume24h,
    decimal Turnover24h);

public sealed record PositionMarketDerivativesResponse(
    decimal FundingRate,
    DateTimeOffset? NextFundingTime,
    decimal OpenInterest,
    decimal OpenInterestValue,
    decimal OpenInterestChange1hPct,
    decimal OpenInterestChange4hPct,
    decimal LongRatio,
    decimal ShortRatio,
    decimal? PremiumVsIndexPct);

public sealed record PositionMarketOrderBookResponse(
    DateTimeOffset CapturedAt,
    decimal BestBidPrice,
    decimal BestAskPrice,
    decimal ImbalanceTop5,
    decimal ImbalanceTop10,
    decimal ImbalanceTop20);

public sealed record PositionMarketTradeFlowResponse(
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    decimal BuyVolume,
    decimal SellVolume,
    decimal DeltaVolume,
    decimal DeltaPct,
    int TotalTrades,
    bool HasAggressiveBuyPressure,
    bool HasAggressiveSellPressure);

public sealed record PositionMarketTimeframeResponse(
    PositionMarketCandleResponse LastCandle,
    decimal? Ema20,
    decimal? Ema50,
    decimal? Ema200,
    decimal? Rsi14,
    decimal? Atr14,
    decimal? VolumeRatio,
    MarketTrendV1 Trend,
    decimal TrendStrength,
    decimal? Support1,
    decimal? Support2,
    decimal? Resistance1,
    decimal? Resistance2,
    decimal? DistanceToSupport1Pct,
    decimal? DistanceToResistance1Pct,
    bool Rsi14IsReliable,
    bool EmaIsReliable,
    bool EmaHasFallback,
    bool AtrIsReliable,
    bool AtrIsFallback,
    bool VolumeRatioIsReliable,
    bool VolumeRatioIsFallback);

public sealed record PositionMarketCandleResponse(
    DateTimeOffset OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover);

public sealed record PositionMarketSentimentResponse(
    MarketRegimeV1 MarketRegime,
    decimal LongShortBiasScore,
    decimal FundingBiasScore,
    decimal OrderBookPressureScore,
    decimal TradeFlowPressureScore);

public sealed record PositionCandlesResponse(
    Guid PositionId,
    ExchangeProvider Exchange,
    string Symbol,
    MarketCategoryV1 MarketCategory,
    string Interval,
    IReadOnlyList<PositionMarketCandleResponse> Items);
