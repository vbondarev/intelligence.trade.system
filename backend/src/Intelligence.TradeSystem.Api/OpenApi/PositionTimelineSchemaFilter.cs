using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionTimelineSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(PositionTimelineItemResponse))
        {
            MakeNullableObjectReference(schema, "positionChange");
            MakeNullableObjectReference(schema, "evaluation");
            MakeNullableObjectReference(schema, "recommendation");
        }
        else if (context.Type == typeof(PositionTimelinePositionChangeResponse))
        {
            MakeNullableObjectReference(schema, "before");
        }
        else if (context.Type == typeof(PositionTimelineRecommendationResponse))
        {
            MakeNullableStringEnumReference(schema, "priority");
        }
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

        schema.Properties[propertyName] = new OpenApiSchema
        {
            Type = referencedType | JsonSchemaType.Null,
            AllOf = [reference],
        };
    }
}
