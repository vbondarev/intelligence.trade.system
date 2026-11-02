using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Intelligence.TradeSystem.Exchanges.Bybit.Public;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelligence.TradeSystem.Exchanges;

public static class StartupExtensions
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddSingleton<BybitPrivateAccountProviderFactory>();
        services.AddSingleton<IPrivateAccountProviderFactory, BybitPrivateAccountProviderFactoryAdapter>();
        services.AddSingleton<IExchangeAccountAccessVerifier, BybitExchangeAccountAccessVerifier>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<BybitInstrumentSettlementResolver>();
        services.AddSingleton<IExchangeRealtimeProviderFactory, BybitRealtimeProviderFactory>();
        services.AddSingleton<BybitPublicMarkPriceProvider>();
        services.AddSingleton<IMarketMarkPriceSubscriptionProvider>(
            serviceProvider => serviceProvider.GetRequiredService<BybitPublicMarkPriceProvider>());
        services.AddScoped<IBybitRestClient>(serviceProvider =>
            BybitClientFactory.CreatePublicClient());
        services.AddScoped<BybitPublicMarketProvider>();
        services.AddScoped<IMarketDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitPublicMarketProvider>());
        services.AddScoped<IDerivativesDataProvider>(serviceProvider => serviceProvider.GetRequiredService<BybitPublicMarketProvider>());

        return services;
    }
}
