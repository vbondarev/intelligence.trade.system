namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountVerificationOutcome
{
    Succeeded,
    NotFound,
    AccountDisabled,
    InvalidCredentials,
    PermissionsRejected,
    ProviderIdentityMismatch,
    CredentialsUnavailable,
    ExchangeUnavailable,
    UnsupportedExchange,
}
