using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Exchanges.Bybit;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Exchanges;

public static class StartupExtensions
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddScoped<IBybitProvider, BybitProvider>();

        return services;
    }
}

