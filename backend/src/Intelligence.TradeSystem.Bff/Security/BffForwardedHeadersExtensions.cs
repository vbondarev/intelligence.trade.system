using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.AspNetCore.HttpOverrides;

namespace Intelligence.TradeSystem.Bff.Security;

internal static class BffForwardedHeadersExtensions
{
    /// <summary>
    /// Принимает public request context от frontend reverse proxy. Defaults ASP.NET Core
    /// (loopback и <see cref="ForwardedHeadersOptions.ForwardLimit"/> = 1) сохраняются: учитывается
    /// только значение, добавленное ближайшим trusted proxy.
    /// </summary>
    internal static IServiceCollection AddBffForwardedHeaders(
        this IServiceCollection services,
        BffConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            foreach (var network in configuration.TrustedProxyNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }
}
