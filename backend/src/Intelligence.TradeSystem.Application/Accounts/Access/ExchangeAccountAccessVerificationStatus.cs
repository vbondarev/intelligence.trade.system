namespace Intelligence.TradeSystem.Application.Accounts.Access;

public enum ExchangeAccountAccessVerificationStatus
{
    Verified = 0,
    InvalidCredentials = 1,
    PermissionsRejected = 2,
    Unavailable = 3,
    UnsupportedExchange = 4,
}
