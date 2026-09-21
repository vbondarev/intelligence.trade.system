using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionEvaluationSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(PositionEvaluationResponse))
            MakeNullable(schema, "recommendation");
        else if (context.Type == typeof(PositionAssessmentResponse))
            MakeNullable(schema, "result");
        else if (context.Type == typeof(PositionRecommendationResponse))
            MakeNullable(schema, "continuation");
        else if (context.Type == typeof(PositionRecommendationActionResponse))
            MakeNullable(schema, "priority");
        else if (context.Type == typeof(PositionRecommendationAddDecisionResponse))
            MakeNullable(schema, "conditions");
    }

    private static void MakeNullable(
        IOpenApiSchema schema,
        string propertyName)
    {
        if (schema.Properties is null ||
            !schema.Properties.TryGetValue(propertyName, out var property) ||
            property is not OpenApiSchemaReference reference)
        {
            return;
        }

        var nullableSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            AllOf = [reference],
        };
        schema.Properties[propertyName] = nullableSchema;
    }
}
