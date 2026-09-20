namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Нейтральная классификация ошибки наблюдения открытых позиций.
/// </summary>
public enum OpenPositionsObservationFailureKind
{
    Unknown = 0,
    InvalidCredentials = 1,
    PermissionDenied = 2,
    RateLimited = 3,
    Unavailable = 4,
}
