using Intelligence.TradeSystem.Application.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Регистрация orchestration-сервисов application-слоя.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Добавляет в контейнер DI сервисы сбора рыночных данных и построения аналитического снапшота.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Ту же коллекцию <see cref="IServiceCollection"/> для fluent-конфигурации.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IMarketDataCollector, MarketDataCollector>();
        services.AddScoped<IMarketAnalysisService, MarketAnalysisService>();
        services.AddScoped<IAiContextFormatter, SnapshotTextFormatter>();

        return services;
    }
}
