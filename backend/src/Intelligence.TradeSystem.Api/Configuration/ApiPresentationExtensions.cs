using System.Reflection;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Api.OpenApi;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.OpenApi;

namespace Intelligence.TradeSystem.Api;

public static class ApiPresentationExtensions
{
    public static IServiceCollection AddApiPresentation(this IServiceCollection services)
    {
        services
            .AddControllers(options =>
            {
                options.OutputFormatters.Insert(0, new V1JsonOutputFormatter());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "OAuth 2.0 / OpenID Connect bearer access token.",
            });
            options.OperationFilter<V1OperationIdOperationFilter>();
            options.OperationFilter<TradeUserAuthorizationOperationFilter>();
            options.OperationFilter<PositionsV1OperationFilter>();

            var xmlFileName = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlFilePath = Path.Combine(AppContext.BaseDirectory, xmlFileName);

            if (File.Exists(xmlFilePath))
            {
                options.IncludeXmlComments(xmlFilePath, includeControllerXmlComments: true);
            }

            options.SchemaFilter<V1EnumSchemaFilter>();
            options.SchemaFilter<ApiProblemDetailsSchemaFilter>();
            options.SchemaFilter<PositionMarketSchemaFilter>();
            options.SchemaFilter<PositionEvaluationSchemaFilter>();
            options.SchemaFilter<PositionTimelineSchemaFilter>();
        });

        return services;
    }

    public static IApplicationBuilder UseApiSwagger(this IApplicationBuilder app)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }
}
