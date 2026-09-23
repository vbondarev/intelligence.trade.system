using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class TradeUserAuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;
        if (metadata is null ||
            metadata.OfType<IAllowAnonymous>().Any() ||
            !metadata.OfType<IAuthorizeData>()
                .Any(data => string.Equals(
                    data.Policy,
                    TradeAuthorization.UserPolicy,
                    StringComparison.Ordinal)))
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [],
        });

        AddResponse(
            operation,
            context.Document,
            StatusCodes.Status401Unauthorized,
            "Authentication is required.");
        AddResponse(
            operation,
            context.Document,
            StatusCodes.Status403Forbidden,
            "The authenticated principal is not a TradeUser.");
    }

    private static void AddResponse(
        OpenApiOperation operation,
        OpenApiDocument document,
        int statusCode,
        string description)
    {
        var key = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        operation.Responses ??= [];
        operation.Responses.TryAdd(key, new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchemaReference("ProblemDetails", document),
                },
            },
        });
    }
}
