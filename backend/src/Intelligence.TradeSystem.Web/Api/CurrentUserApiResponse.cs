namespace Intelligence.TradeSystem.Web.Api;

/// <summary>
/// Ответ существующего <c>GET /api/v1/auth/me</c>, который BFF читает от имени browser session.
/// </summary>
internal sealed record CurrentUserApiResponse(Guid UserId, string Subject, bool Authenticated);
