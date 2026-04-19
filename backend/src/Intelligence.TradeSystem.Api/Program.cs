using Intelligence.TradeSystem.Ai;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Analytics;
using Intelligence.TradeSystem.Exchanges;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlFilePath = Path.Combine(AppContext.BaseDirectory, xmlFileName);

            if (File.Exists(xmlFilePath))
            {
                options.IncludeXmlComments(xmlFilePath, includeControllerXmlComments: true);
            }
        });
        builder.Services.AddSingleton(_ => builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions());
        builder.Services.AddHttpClient<IOpenRouterClient, OpenRouterClient>();
        builder.Services.AddAnalytics();
        builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
        builder.Services.AddScoped<ILlmAnalyticsService, LlmAnalyticsService>();
        builder.Services.AddApplication();
        builder.Services.AddBybitExchange();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapGet("/", () => Results.Ok(new
        {
            Service = "Intelligence.TradeSystem.Api",
            Status = "Started",
        }));

        app.MapControllers();
        app.MapDefaultEndpoints();

        app.Run();
    }
}
