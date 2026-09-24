using System.Reflection;
using Intelligence.TradeSystem.Api.OpenApi;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.OpenApi;

namespace Intelligence.TradeSystem.Api;

public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
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
}
