using System.Reflection;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Analytics;
using Intelligence.TradeSystem.Api.Configuration;
using Intelligence.TradeSystem.Api.Services;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Exchanges;
using Microsoft.AspNetCore.Mvc;

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
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var query = context.HttpContext.Request.Query;

                    var hasModeError = context.ModelState.Any(e =>
                        e.Key.Equals("mode", StringComparison.OrdinalIgnoreCase) &&
                        e.Value?.Errors.Count > 0);

                    if (hasModeError && query.ContainsKey("mode"))
                    {
                        var detail = $"Field 'mode' has invalid value '{query["mode"]}'. " +
                                     "Allowed values: Intraday, Swing, Portfolio.";
                        return new BadRequestObjectResult(new ProblemDetails
                        {
                            Status = StatusCodes.Status400BadRequest,
                            Title = "Request validation failed.",
                            Detail = detail,
                        });
                    }

                    // Default behaviour for all other binding/validation errors.
                    return new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState)
                    {
                        Status = StatusCodes.Status400BadRequest,
                    });
                };
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
        builder.Services.AddAnalytics();
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
