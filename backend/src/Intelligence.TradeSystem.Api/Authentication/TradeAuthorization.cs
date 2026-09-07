using System.Security.Claims;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Authentication;

internal static class TradeAuthorization
{
    internal const string ApiPolicy = "TradeApi";
    internal const string UserPolicy = "TradeUser";
    internal const string PrincipalTypeClaim = "trade_principal_type";
    internal const string UserPrincipalType = "user";
    internal const string ScopeClaim = "scope";
    internal const string SubjectClaim = "sub";

    internal static bool HasApiScope(ClaimsPrincipal principal) =>
        principal.FindAll(ScopeClaim)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains("trade.api", StringComparer.Ordinal);

    internal static bool TryGetUserId(ClaimsPrincipal principal, out UserId userId)
    {
        userId = default;

        if (!principal.HasClaim(PrincipalTypeClaim, UserPrincipalType)
            || !Guid.TryParse(principal.FindFirst(SubjectClaim)?.Value, out var value)
            || value == Guid.Empty)
        {
            return false;
        }

        userId = UserId.FromGuid(value);
        return true;
    }
}
