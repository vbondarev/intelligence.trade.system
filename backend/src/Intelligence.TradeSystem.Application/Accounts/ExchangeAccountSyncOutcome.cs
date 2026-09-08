namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountSyncOutcome
{
    Synchronized = 0,
    NotFound = 1,
    AccountDisabled = 2,
    CredentialsUnavailable = 3,
    ExchangeUnavailable = 4,
}
