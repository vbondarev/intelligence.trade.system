using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.Serialization;

internal sealed class V1EnumSchemaFilter : ISchemaFilter
{
    private const string V1ContractsNamespace = "Intelligence.TradeSystem.Api.Contracts.V1.";

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum
            || context.Type.Namespace is not { } typeNamespace
            || !typeNamespace.StartsWith(V1ContractsNamespace, StringComparison.Ordinal))
        {
            return;
        }

        if (schema.Enum is null)
        {
            return;
        }

        schema.Enum.Clear();
        foreach (var enumName in Enum.GetNames(context.Type))
        {
            schema.Enum.Add(
                JsonValue.Create(JsonNamingPolicy.CamelCase.ConvertName(enumName))!);
        }
    }
}
