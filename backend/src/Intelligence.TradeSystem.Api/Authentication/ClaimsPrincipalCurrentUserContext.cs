using Intelligence.TradeSystem.Application.Users;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Authentication;

internal sealed class ClaimsPrincipalCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    : ICurrentUserContext
{
    public UserId UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true
                || !TradeAuthorization.TryGetUserId(principal, out var userId))
            {
                throw new InvalidCurrentUserException(
                    "The current principal is not a valid user principal.");
            }

            return userId;
        }
    }
}
