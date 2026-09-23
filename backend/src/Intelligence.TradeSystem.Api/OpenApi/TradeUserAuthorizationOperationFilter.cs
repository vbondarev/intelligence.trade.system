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

        NormalizeUnauthorizedResponse(
            operation,
            StatusCodes.Status401Unauthorized);
        AddResponse(
            operation,
            StatusCodes.Status403Forbidden,
            "The authenticated principal is not a TradeUser.");
    }

    private static void NormalizeUnauthorizedResponse(
        OpenApiOperation operation,
        int statusCode)
    {
        var key = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation.Responses?.TryGetValue(key, out var response) == true)
        {
            response.Content?.Clear();
            return;
        }

        AddResponse(operation, statusCode, "Authentication is required.");
    }

    private static void AddResponse(
        OpenApiOperation operation,
        int statusCode,
        string description)
    {
        var key = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        operation.Responses ??= [];
        operation.Responses.TryAdd(key, new OpenApiResponse
        {
            Description = description,
        });
    }
}
