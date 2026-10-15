using Intelligence.TradeSystem.Application.AI;
using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Market.Positions;
using Intelligence.TradeSystem.Application.Realtime;
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
        services.AddScoped<PositionMarketService>(
            serviceProvider => ActivatorUtilities.CreateInstance<PositionMarketService>(serviceProvider));
        services.AddScoped<PositionAssessmentService>();
        services.AddSingleton<RecommendationPolicy>();
        services.AddSingleton<RecommendationStabilityPolicy>();
        services.AddScoped<IExchangeAccountService>(
            serviceProvider => ActivatorUtilities.CreateInstance<ExchangeAccountService>(serviceProvider));
        services.AddScoped<ExchangeAccountSyncService>(
            serviceProvider => ActivatorUtilities.CreateInstance<ExchangeAccountSyncService>(serviceProvider));
        services.AddScoped<RealtimePositionReconciliationService>(
            serviceProvider => ActivatorUtilities.CreateInstance<RealtimePositionReconciliationService>(serviceProvider));
        services.AddScoped<RealtimeWalletObservationService>(
            serviceProvider => ActivatorUtilities.CreateInstance<RealtimeWalletObservationService>(serviceProvider));
        // Все REST-синхронизации аккаунта (ручная, плановая, стартовая и recovery) проходят через
        // единый координатор, который упорядочивает их с потоковыми наблюдениями.
        services.AddSingleton<ExchangeAccountObservationCoordinator>(
            serviceProvider => ActivatorUtilities.CreateInstance<ExchangeAccountObservationCoordinator>(serviceProvider));
        services.AddSingleton<IExchangeAccountSyncService>(
            serviceProvider => serviceProvider.GetRequiredService<ExchangeAccountObservationCoordinator>());

        return services;
    }
}
