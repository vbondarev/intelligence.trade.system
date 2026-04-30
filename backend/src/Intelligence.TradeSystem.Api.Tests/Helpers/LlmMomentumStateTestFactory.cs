using Intelligence.TradeSystem.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

/// <summary>
/// Специализированная фабрика для тестов <c>LlmMomentumStateMappingTests</c>.
/// Регистрирует <see cref="ConfigurableMarketAnalysisService"/> один раз при старте хоста,
/// что позволяет переиспользовать единственный тестовый хост для всего тест-класса.
/// </summary>
public sealed class LlmMomentumStateTestFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Сервис с заменяемым снапшотом. Тест конфигурирует его перед каждым HTTP-запросом.
    /// </summary>
    public ConfigurableMarketAnalysisService MarketService { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMarketAnalysisService>();
            services.AddSingleton<IMarketAnalysisService>(MarketService);
        });
}

