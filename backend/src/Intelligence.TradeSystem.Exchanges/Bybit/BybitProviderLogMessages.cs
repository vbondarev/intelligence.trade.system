using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.Logging;
using KlineInterval = Intelligence.TradeSystem.Domain.KlineInterval;

namespace Intelligence.TradeSystem.Exchanges.Bybit;

internal sealed partial class BybitProvider
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Failed to fetch klines for {Symbol} ({Category}, {Interval}): {Error}")]
    private static partial void LogFailedToFetchKlines(
        ILogger logger,
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        string? error);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Failed to fetch spot ticker for {Symbol}: {Error}")]
    private static partial void LogFailedToFetchSpotTicker(
        ILogger logger,
        string symbol,
        string? error);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Failed to fetch ticker for {Symbol} ({Category}): {Error}")]
    private static partial void LogFailedToFetchTicker(
        ILogger logger,
        string symbol,
        MarketCategory category,
        string? error);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Error,
        Message = "Failed to fetch order book for {Symbol} ({Category}): {Error}")]
    private static partial void LogFailedToFetchOrderBook(
        ILogger logger,
        string symbol,
        MarketCategory category,
        string? error);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Error,
        Message = "Failed to fetch recent trades for {Symbol} ({Category}): {Error}")]
    private static partial void LogFailedToFetchRecentTrades(
        ILogger logger,
        string symbol,
        MarketCategory category,
        string? error);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Error,
        Message = "Failed to fetch open interest for {Symbol} ({Category}, {Interval}): {Error}")]
    private static partial void LogFailedToFetchOpenInterest(
        ILogger logger,
        string symbol,
        MarketCategory category,
        OpenInterestInterval interval,
        string? error);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Failed to fetch funding rate history for {Symbol} ({Category}): {Error}")]
    private static partial void LogFailedToFetchFundingRateHistory(
        ILogger logger,
        string symbol,
        MarketCategory category,
        string? error);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Error,
        Message = "Failed to fetch long/short ratio for {Symbol} ({Category}, {Period}): {Error}")]
    private static partial void LogFailedToFetchLongShortRatio(
        ILogger logger,
        string symbol,
        MarketCategory category,
        LongShortRatioPeriod period,
        string? error);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Error,
        Message = "Failed to fetch open positions ({Category}, {Symbol}): {Error}")]
    private static partial void LogFailedToFetchOpenPositions(
        ILogger logger,
        MarketCategory category,
        string symbol,
        string? error);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Error,
        Message = "Failed to fetch wallet balance ({AccountType}): {Error}")]
    private static partial void LogFailedToFetchWalletBalance(
        ILogger logger,
        AccountType accountType,
        string? error);
}

