using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Api.Validation;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Exchanges;

namespace Intelligence.TradeSystem.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        builder.Services
            .AddControllers()
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
        builder.Services.AddApplication();
        builder.Services.AddBybitExchange();
        var freshnessOptions = builder.Configuration
            .GetSection(SnapshotFreshnessOptions.SectionName)
            .Get<SnapshotFreshnessOptions>() ?? SnapshotFreshnessOptions.Default;
        builder.Services.AddOptions<SnapshotFreshnessOptions>().Configure(o =>
        {
            // no-op: options bound via singleton below
        });
        builder.Services.AddSingleton(freshnessOptions);
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IOptions<SnapshotFreshnessOptions>>(
            sp => Microsoft.Extensions.Options.Options.Create(sp.GetRequiredService<SnapshotFreshnessOptions>()));
        builder.Services.AddSingleton<ISnapshotHealthEvaluator, SnapshotHealthEvaluator>();
        builder.Services.AddScoped<IValidator<SnapshotAnalysisRequest>, SnapshotAnalysisRequestValidator>();
        builder.Services.AddScoped<IValidator<LlmPayloadRequest>, LlmPayloadRequestValidator>();

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
