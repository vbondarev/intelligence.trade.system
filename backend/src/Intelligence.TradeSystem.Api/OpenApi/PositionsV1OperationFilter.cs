using System.Text.Json.Nodes;
using System.Globalization;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Market.Positions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class PositionsV1OperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.Equals(context.ApiDescription.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = context.ApiDescription.RelativePath?.Trim('/');
        if (string.Equals(path, "api/v1/positions", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPositionList(operation);
        }
        else if (string.Equals(
                     path,
                     "api/v1/positions/{id}/candles",
                     StringComparison.OrdinalIgnoreCase))
        {
            ApplyCandles(operation);
        }
        else if (string.Equals(
                     path,
                     "api/v1/positions/{id}/timeline",
                     StringComparison.OrdinalIgnoreCase))
        {
            ApplyTimeline(operation);
        }
    }

    private static void ApplyPositionList(OpenApiOperation operation)
    {
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

    private static void ApplyCandles(OpenApiOperation operation)
    {
        SetEnum(operation, "interval", [.. CandleIntervalV1Codec.AllWireValues]);
        var interval = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "interval", StringComparison.Ordinal));
        if (interval is OpenApiParameter intervalParameter)
        {
            intervalParameter.Required = true;
        }

        var limit = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "limit", StringComparison.Ordinal));
        if (limit?.Schema is not OpenApiSchema limitSchema)
        {
            return;
        }

        limitSchema.Minimum = "1";
        limitSchema.Maximum = PositionMarketService.MaxCandleLimit.ToString(CultureInfo.InvariantCulture);
        limitSchema.Default = JsonValue.Create(PositionMarketService.DefaultCandleLimit);
    }

    private static void ApplyTimeline(OpenApiOperation operation)
    {
        var pageSize = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "pageSize", StringComparison.Ordinal));
        if (pageSize?.Schema is OpenApiSchema pageSizeSchema)
        {
            pageSizeSchema.Minimum = "1";
            pageSizeSchema.Maximum = "100";
            pageSizeSchema.Default = JsonValue.Create(50);
        }

        var cursor = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "cursor", StringComparison.Ordinal));
        if (cursor is OpenApiParameter cursorParameter)
        {
            cursorParameter.Description = "Opaque cursor returned by a preceding timeline page.";
        }

        var type = operation.Parameters?
            .SingleOrDefault(parameter =>
                string.Equals(parameter.Name, "type", StringComparison.Ordinal));
        if (type?.Schema?.Items is not OpenApiSchema itemSchema)
        {
            return;
        }

        itemSchema.Type = JsonSchemaType.String;
        itemSchema.Enum ??= [];
        itemSchema.Enum.Clear();
        foreach (var value in new[] { "positionChange", "evaluation", "recommendation" })
        {
            itemSchema.Enum.Add(JsonValue.Create(value)!);
        }
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
