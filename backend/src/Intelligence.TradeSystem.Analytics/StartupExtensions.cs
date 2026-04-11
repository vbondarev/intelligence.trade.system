using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Analytics;

/// <summary>
/// Регистрация сервисов аналитического слоя, интерпретирующих готовый <c>MarketAnalysisSnapshot</c>
/// и подготавливающих downstream-friendly analytics output.
/// </summary>
public static class StartupExtensions
{
    /// <summary>
    /// Добавляет в контейнер DI сервисы аналитического слоя:
    /// классификацию рыночного режима, форматирование компактного контекста и
    /// orchestration-объединение этих результатов.
    /// </summary>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Ту же коллекцию <see cref="IServiceCollection"/> для fluent-конфигурации.</returns>
    public static IServiceCollection AddAnalytics(this IServiceCollection services)
    {
        services.AddScoped<IAnalyticsFormatter, SnapshotTextFormatter>();
        services.AddScoped<IMarketRegimeClassifier, MarketRegimeClassifier>();
        services.AddScoped<IAnalyticsOutputComposer, AnalyticsOutputComposer>();

        return services;
    }
}

