using Intelligence.TradeSystem.Application.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Регистрация orchestration-сервисов application-слоя.
/// </summary>
public static class StartupExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPublicMarketDataCollector, PublicMarketDataCollector>();
        services.AddScoped<IMarketSnapshotService, MarketSnapshotService>();
        services.AddScoped<IAiContextFormatter, SnapshotTextFormatter>();

        return services;
    }
}
