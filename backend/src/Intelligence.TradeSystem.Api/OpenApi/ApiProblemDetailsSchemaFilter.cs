using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class ApiProblemDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(ProblemDetails) || schema.Properties is null)
        {
            return;
        }

        schema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable machine-readable application error code.",
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Request trace identifier.",
        };
        schema.Properties["reason"] =
            context.SchemaGenerator.GenerateSchema(
                typeof(PositionNotEvaluableReasonV1),
                context.SchemaRepository);
    }
}
