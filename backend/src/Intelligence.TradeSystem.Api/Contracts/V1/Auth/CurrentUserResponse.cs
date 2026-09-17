namespace Intelligence.TradeSystem.Api.Contracts.V1.Auth;

/// <summary>
/// Стабильный пользовательский v1-контракт текущей аутентифицированной identity.
/// </summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Subject,
    bool Authenticated);
