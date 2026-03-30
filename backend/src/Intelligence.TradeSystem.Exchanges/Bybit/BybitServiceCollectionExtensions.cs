using Intelligence.TradeSystem.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Exchanges.Bybit;

public static class BybitServiceCollectionExtensions
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddScoped<IBybitProvider, BybitBybitProvider>();

        return services;
    }
}

