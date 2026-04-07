using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Exchanges.Bybit;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Exchanges;

public static class StartupExtensions
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddScoped<BybitProvider>();
        services.AddScoped<IMarketDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitProvider>());
        services.AddScoped<IDerivativesDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitProvider>());
        services.AddScoped<IPrivateAccountProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitProvider>());
        services.AddScoped<IBybitProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitProvider>());

        return services;
    }
}

