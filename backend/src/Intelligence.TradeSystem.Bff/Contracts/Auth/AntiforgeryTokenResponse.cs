namespace Intelligence.TradeSystem.Bff.Contracts.Auth;

/// <summary>
/// Request token для header <c>X-CSRF-TOKEN</c> state-changing BFF-запросов.
/// </summary>
public sealed record AntiforgeryTokenResponse(string RequestToken);
