using Intelligence.TradeSystem.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        AddProblemDetailsResponse(
            operation,
            StatusCodes.Status401Unauthorized,
            "Authentication is required; code: authentication_required.",
            context);
        AddProblemDetailsResponse(
            operation,
            StatusCodes.Status403Forbidden,
            "Authorization policy denied access; code: access_forbidden. Endpoint-specific business errors retain their own codes.",
            context);
        AddUnauthorizedHeader(operation);
    }

    private static void AddProblemDetailsResponse(
        OpenApiOperation operation,
        int statusCode,
        string description,
        OperationFilterContext context)
    {
        var key = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        operation.Responses ??= [];
        operation.Responses.TryGetValue(key, out var existingResponse);
        var content = existingResponse?.Content?.ToDictionary(
            item => item.Key,
            item => item.Value)
            ?? new Dictionary<string, OpenApiMediaType>();
        if (!string.IsNullOrWhiteSpace(existingResponse?.Description))
        {
            description = $"{existingResponse.Description} {description}";
        }

        content["application/problem+json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(
                typeof(ProblemDetails),
                context.SchemaRepository),
        };
        operation.Responses[key] = new OpenApiResponse
        {
            Description = description,
            Content = content,
            Headers = existingResponse?.Headers?.ToDictionary(
                item => item.Key,
                item => item.Value)
                ?? new Dictionary<string, IOpenApiHeader>(),
        };
    }

    private static void AddUnauthorizedHeader(OpenApiOperation operation)
    {
        var key = StatusCodes.Status401Unauthorized.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (operation.Responses?.TryGetValue(key, out var existingResponse) != true
            || existingResponse is null)
        {
            throw new InvalidOperationException("The v1 authentication response was not registered.");
        }

        var headers = existingResponse.Headers?.ToDictionary(
            item => item.Key,
            item => item.Value)
            ?? new Dictionary<string, IOpenApiHeader>();
        headers["WWW-Authenticate"] = new OpenApiHeader
        {
            Description = "Bearer authentication challenge.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };
        operation.Responses[key] = new OpenApiResponse
        {
            Description = existingResponse.Description,
            Content = existingResponse.Content?.ToDictionary(
                item => item.Key,
                item => item.Value)
                ?? new Dictionary<string, OpenApiMediaType>(),
            Headers = headers,
        };
    }
}
