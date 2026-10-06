namespace Intelligence.TradeSystem.Bff.Contracts.Auth;

/// <summary>
/// Локальный адрес второй фазы logout, на который React выполняет top-level navigation.
/// </summary>
public sealed record LogoutResponse(string RedirectUrl);
