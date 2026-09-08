namespace Intelligence.TradeSystem.Application.Accounts;

public enum ExchangeAccountConnectionOutcome
{
    Connected = 0,
    InvalidCredentials = 1,
    PermissionsRejected = 2,
    Unavailable = 3,
    UnsupportedExchange = 4,
}
