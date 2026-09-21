using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionEvaluationSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(PositionEvaluationResponse))
            MakeNullableObjectReference(schema, "recommendation");
        else if (context.Type == typeof(PositionAssessmentResponse))
            MakeNullableObjectReference(schema, "result");
        else if (context.Type == typeof(PositionRecommendationResponse))
            MakeNullableObjectReference(schema, "continuation");
        else if (context.Type == typeof(PositionRecommendationActionResponse))
            MakeNullableStringEnumReference(schema, "priority");
        else if (context.Type == typeof(PositionRecommendationAddDecisionResponse))
            MakeNullableObjectReference(schema, "conditions");
    }

    private static void MakeNullableObjectReference(
        IOpenApiSchema schema,
        string propertyName) =>
        MakeNullableReference(schema, propertyName, JsonSchemaType.Object);

    private static void MakeNullableStringEnumReference(
        IOpenApiSchema schema,
        string propertyName) =>
        MakeNullableReference(schema, propertyName, JsonSchemaType.String);

    private static void MakeNullableReference(
        IOpenApiSchema schema,
        string propertyName,
        JsonSchemaType referencedType)
    {
        if (schema.Properties is null ||
            !schema.Properties.TryGetValue(propertyName, out var property) ||
            property is not OpenApiSchemaReference reference)
        {
            return;
        }

        var nullableSchema = new OpenApiSchema
        {
            Type = referencedType | JsonSchemaType.Null,
            AllOf = [reference],
        };
        schema.Properties[propertyName] = nullableSchema;
    }
}
