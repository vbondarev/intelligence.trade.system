using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

internal static class ServiceProviderExtensions
{
    public static ITicketStore GetTicketStore(this IServiceProvider services) =>
        services.GetRequiredService<ITicketStore>();
}
