using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionsV1OperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                context.ApiDescription.RelativePath?.Trim('/'),
                "api/v1/positions",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetEnum(
            operation,
            "trackingState",
            "active",
            "unknown",
            "stale",
            "closed");
        SetEnum(operation, "side", "long", "short");

        var pageSize = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "pageSize", StringComparison.Ordinal));
        if (pageSize?.Schema is not OpenApiSchema pageSizeSchema)
        {
            return;
        }

        pageSizeSchema.Minimum = "1";
        pageSizeSchema.Maximum = "100";
        pageSizeSchema.Default = JsonValue.Create(50);
    }

    private static void SetEnum(
        OpenApiOperation operation,
        string parameterName,
        params string[] values)
    {
        var parameter = operation.Parameters?
            .SingleOrDefault(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));
        if (parameter?.Schema is not OpenApiSchema schema)
        {
            return;
        }

        schema.Type = JsonSchemaType.String;
        schema.Enum ??= [];
        schema.Enum.Clear();
        foreach (var value in values)
        {
            schema.Enum.Add(JsonValue.Create(value)!);
        }
    }
}
