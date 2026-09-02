using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal static partial class BybitPrivateProviderLogMessages
{
    [LoggerMessage(EventId = 1009, Level = LogLevel.Error, Message = "Failed to fetch open positions ({Category}, {Symbol}): {Error}")]
    internal static partial void LogFailedToFetchOpenPositions(ILogger logger, MarketCategory category, string symbol, string? error);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "Failed to fetch wallet balance ({AccountType}): {Error}")]
    internal static partial void LogFailedToFetchWalletBalance(ILogger logger, AccountType accountType, string? error);
}
