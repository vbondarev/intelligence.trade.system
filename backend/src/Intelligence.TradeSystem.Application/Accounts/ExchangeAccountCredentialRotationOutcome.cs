namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountCredentialRotationOutcome
{
    Succeeded,
    NotFound,
    AccountDisabled,
    PermissionsRejected,
    InvalidCredentials,
    CredentialsUnavailable,
    ExchangeUnavailable,
    UnsupportedExchange,
}
