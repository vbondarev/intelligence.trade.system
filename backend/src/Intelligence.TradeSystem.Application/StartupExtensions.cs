using Intelligence.TradeSystem.Application.AI;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Application.Recommendations;
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
        services.AddScoped<MarketSnapshotService>();
        services.AddScoped<IPublicMarketSnapshotBuilder>(
            serviceProvider => serviceProvider.GetRequiredService<MarketSnapshotService>());
        services.AddScoped<IMarketSnapshotService>(
            serviceProvider => serviceProvider.GetRequiredService<MarketSnapshotService>());
        services.AddScoped<IAiContextFormatter, SnapshotTextFormatter>();
        services.AddScoped<PositionAssessmentService>();
        services.AddSingleton<RecommendationPolicy>();
        services.AddScoped<IExchangeAccountService>(
            serviceProvider => ActivatorUtilities.CreateInstance<ExchangeAccountService>(serviceProvider));
        services.AddScoped<IExchangeAccountSyncService>(
            serviceProvider => ActivatorUtilities.CreateInstance<ExchangeAccountSyncService>(serviceProvider));

        return services;
    }
}
