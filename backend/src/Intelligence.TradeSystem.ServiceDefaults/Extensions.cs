using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Intelligence.TradeSystem.ServiceDefaults;

// Добавляет общие сервисы .NET Aspire: service discovery, resilience, health checks и OpenTelemetry.
// Этот проект следует подключать к каждому service project в решении.
// Подробнее об использовании проекта см. https://aka.ms/dotnet/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/healthz";
    private const string AlivenessEndpointPath = "/alive";
    private const string BybitExchangeTelemetryName = "Intelligence.TradeSystem.Exchanges.Bybit";
    private const string BackgroundSyncTelemetryName =
        "Intelligence.TradeSystem.Infrastructure.BackgroundSync";
    private const string ApplicationEventsTelemetryName =
        "Intelligence.TradeSystem.Infrastructure.ApplicationEvents";
    private const string PublicMarketSnapshotCacheTelemetryName =
        "Intelligence.TradeSystem.Infrastructure.MarketCaching";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Включить resilience по умолчанию
            http.AddStandardResilienceHandler();

            // Включаем service discovery по умолчанию
            http.AddServiceDiscovery();
        });

        // Раскомментируйте следующую строку, чтобы ограничить разрешённые схемы service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    private static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(BybitExchangeTelemetryName)
                    .AddMeter(BackgroundSyncTelemetryName)
                    .AddMeter(ApplicationEventsTelemetryName)
                    .AddMeter(PublicMarketSnapshotCacheTelemetryName);
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddSource(BybitExchangeTelemetryName)
                    .AddSource(BackgroundSyncTelemetryName)
                    .AddSource(ApplicationEventsTelemetryName)
                    .AddSource(PublicMarketSnapshotCacheTelemetryName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Исключить запросы health checks из трассировки
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Раскомментируйте следующую строку, чтобы включить gRPC instrumentation
                    // (требуется пакет OpenTelemetry.Instrumentation.GrpcNetClient).
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Раскомментируйте следующие строки, чтобы включить Azure Monitor exporter
        // (требуется пакет Azure.Monitor.OpenTelemetry.AspNetCore).
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    private static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services
            .AddHealthChecks()
            // Добавить стандартную проверку работоспособности (liveness), чтобы убедиться, что приложение отвечает
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // После запуска все health checks должны пройти, чтобы приложение считалось готовым принимать трафик
        app.MapHealthChecks(HealthEndpointPath);

        // Для признания приложения работающим должны пройти только health checks с тегом "live"
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
