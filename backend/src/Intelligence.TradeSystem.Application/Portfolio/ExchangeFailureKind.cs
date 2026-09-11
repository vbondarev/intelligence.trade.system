namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Категории ошибок биржи, понятные прикладной границе.
/// </summary>
public enum ExchangeFailureKind
{
    Unknown = 0,
    InvalidCredentials = 1,
    PermissionDenied = 2,
    RateLimited = 3,
    Timeout = 4,
    Unavailable = 5,
    InvalidResponse = 6,
}
