using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Intelligence.TradeSystem.Exchanges.Bybit.Public;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Exchanges;

public static class StartupExtensions
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddSingleton<BybitPrivateAccountProviderFactory>();
        services.AddScoped<IBybitRestClient>(serviceProvider =>
            BybitClientFactory.CreatePublicClient());
        services.AddScoped<BybitPublicMarketProvider>();
        services.AddScoped<IMarketDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitPublicMarketProvider>());
        services.AddScoped<IDerivativesDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitPublicMarketProvider>());

        return services;
    }
}
