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
    }
}
