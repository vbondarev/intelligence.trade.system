using Intelligence.TradeSystem.Api.Authentication;
using Intelligence.TradeSystem.Application.Users;

namespace Intelligence.TradeSystem.Api.Services;

public static class CurrentUserServiceCollectionExtensions
{
    public static IServiceCollection AddCurrentUserContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, ClaimsPrincipalCurrentUserContext>();

        return services;
    }
}
