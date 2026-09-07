namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Exchange failure categories understood by the application boundary.
/// </summary>
public enum ExchangeFailureKind
{
    InvalidCredentials = 0,
    PermissionDenied = 1,
    RateLimited = 2,
    Timeout = 3,
    Unavailable = 4,
    InvalidResponse = 5,
    Unknown = 6,
}
