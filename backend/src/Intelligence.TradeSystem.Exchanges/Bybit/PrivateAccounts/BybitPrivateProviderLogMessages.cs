using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Application.Portfolio;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal static partial class BybitPrivateProviderLogMessages
{
    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Warning,
        Message = "Exchange operation failed. Operation={Operation}, Exchange={Exchange}, MarketCategory={MarketCategory}, Symbol={Symbol}, FailureKind={FailureKind}, Retryable={Retryable}, ProviderCode={ProviderCode}, Outcome={Outcome}, ElapsedMs={ElapsedMs}")]
    internal static partial void LogFailedToFetchOpenPositions(
        ILogger logger,
        string operation,
        string exchange,
        MarketCategory marketCategory,
        string symbol,
        ExchangeFailureKind failureKind,
        bool retryable,
        string? providerCode,
        string outcome,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Warning,
        Message = "Exchange operation failed. Operation={Operation}, Exchange={Exchange}, AccountType={AccountType}, FailureKind={FailureKind}, Retryable={Retryable}, ProviderCode={ProviderCode}, Outcome={Outcome}, ElapsedMs={ElapsedMs}")]
    internal static partial void LogFailedToFetchWalletBalance(
        ILogger logger,
        string operation,
        string exchange,
        AccountType accountType,
        ExchangeFailureKind failureKind,
        bool retryable,
        string? providerCode,
        string outcome,
        double elapsedMs);
}
