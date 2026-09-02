using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Public;

internal static partial class BybitPublicProviderLogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Failed to fetch klines for {Symbol} ({Category}, {Interval}): {Error}")]
    internal static partial void LogFailedToFetchKlines(ILogger logger, string symbol, MarketCategory category, KlineInterval interval, string? error);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Failed to fetch spot ticker for {Symbol}: {Error}")]
    internal static partial void LogFailedToFetchSpotTicker(ILogger logger, string symbol, string? error);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Failed to fetch ticker for {Symbol} ({Category}): {Error}")]
    internal static partial void LogFailedToFetchTicker(ILogger logger, string symbol, MarketCategory category, string? error);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Failed to fetch order book for {Symbol} ({Category}): {Error}")]
    internal static partial void LogFailedToFetchOrderBook(ILogger logger, string symbol, MarketCategory category, string? error);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Failed to fetch recent trades for {Symbol} ({Category}): {Error}")]
    internal static partial void LogFailedToFetchRecentTrades(ILogger logger, string symbol, MarketCategory category, string? error);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "Failed to fetch open interest for {Symbol} ({Category}, {Interval}): {Error}")]
    internal static partial void LogFailedToFetchOpenInterest(ILogger logger, string symbol, MarketCategory category, OpenInterestInterval interval, string? error);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "Failed to fetch funding rate history for {Symbol} ({Category}): {Error}")]
    internal static partial void LogFailedToFetchFundingRateHistory(ILogger logger, string symbol, MarketCategory category, string? error);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Error, Message = "Failed to fetch long/short ratio for {Symbol} ({Category}, {Period}): {Error}")]
    internal static partial void LogFailedToFetchLongShortRatio(ILogger logger, string symbol, MarketCategory category, LongShortRatioPeriod period, string? error);
}
