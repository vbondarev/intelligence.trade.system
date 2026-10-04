using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Web.Tests.Support;

internal static class ServiceProviderExtensions
{
    public static ITicketStore GetTicketStore(this IServiceProvider services) =>
        services.GetRequiredService<ITicketStore>();
}
