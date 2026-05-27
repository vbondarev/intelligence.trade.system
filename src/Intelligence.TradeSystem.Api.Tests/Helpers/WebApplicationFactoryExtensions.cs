using Intelligence.TradeSystem.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Intelligence.TradeSystem.Api.Tests.Helpers;

internal static class WebApplicationFactoryExtensions
{
    public static HttpClient CreateClientWithMarketAnalysisService(
        this WebApplicationFactory<Program> factory,
        IMarketAnalysisService marketAnalysisService) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMarketAnalysisService>();
                services.AddSingleton(marketAnalysisService);
            }))
        .CreateClient();
}
