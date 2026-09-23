using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Intelligence.TradeSystem.Api.OpenApi;

internal sealed class V1OperationIdOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (V1OperationIds.TryGet(
                context.ApiDescription.HttpMethod,
                context.ApiDescription.RelativePath,
                out var operationId))
        {
            operation.OperationId = operationId;
        }
    }
}
