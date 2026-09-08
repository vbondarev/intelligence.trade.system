using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Accounts;

public sealed record ExchangeAccountSyncResult(
    ExchangeAccountSyncOutcome Outcome,
    ExchangeAccount? Account,
    PortfolioState? PortfolioState)
{
    public static ExchangeAccountSyncResult Synchronized(
        ExchangeAccount account,
        PortfolioState portfolioState) =>
        new(ExchangeAccountSyncOutcome.Synchronized, account, portfolioState);

    public static ExchangeAccountSyncResult NotFound() =>
        new(ExchangeAccountSyncOutcome.NotFound, null, null);

    public static ExchangeAccountSyncResult AccountDisabled() =>
        new(ExchangeAccountSyncOutcome.AccountDisabled, null, null);

    public static ExchangeAccountSyncResult CredentialsUnavailable() =>
        new(ExchangeAccountSyncOutcome.CredentialsUnavailable, null, null);

    public static ExchangeAccountSyncResult ExchangeUnavailable() =>
        new(ExchangeAccountSyncOutcome.ExchangeUnavailable, null, null);
}
