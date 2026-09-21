using System.Text.Json.Nodes;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions.Market;
using Intelligence.TradeSystem.Api.Serialization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionMarketSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(PositionCandlesResponse) ||
            schema.Properties is null ||
            !schema.Properties.TryGetValue("interval", out var intervalSchema) ||
            intervalSchema is not OpenApiSchema concreteIntervalSchema)
        {
            return;
        }

        concreteIntervalSchema.Type = JsonSchemaType.String;
        concreteIntervalSchema.Enum ??= [];
        concreteIntervalSchema.Enum.Clear();
        foreach (var value in CandleIntervalV1Codec.AllWireValues)
        {
            concreteIntervalSchema.Enum.Add(JsonValue.Create(value)!);
        }
    }
}
