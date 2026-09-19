namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountCredentialRotationOutcome
{
    Succeeded,
    NotFound,
    AccountDisabled,
    PermissionsRejected,
    ProviderIdentityMismatch,
    InvalidCredentials,
    CredentialsUnavailable,
    ExchangeUnavailable,
    UnsupportedExchange,
}
