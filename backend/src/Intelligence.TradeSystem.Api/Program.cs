using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Ai;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Analytics;
using Intelligence.TradeSystem.Exchanges;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddControllers();
        builder.Services.AddSingleton(_ => builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions());
        builder.Services.AddHttpClient<IOpenRouterClient, OpenRouterClient>();
        builder.Services.AddAnalytics();
        builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
        builder.Services.AddScoped<ILlmAnalyticsService, LlmAnalyticsService>();
        builder.Services.AddApplication();
        builder.Services.AddBybitExchange();

        var app = builder.Build();

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Started",
        }));

        app.MapGet("/health", static () => Results.Ok(new HealthResponse
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Healthy",
        }));

        app.MapControllers();

        app.Run();
    }
}
