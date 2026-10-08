using Intelligence.TradeSystem.Bff.Api;

namespace Intelligence.TradeSystem.Bff.Endpoints;

/// <summary>
/// Browser-facing чтение списка позиций текущего пользователя. Endpoint соответствует одной
/// заранее определённой операции API; query ограничен allowlist <see cref="PositionsApiClient"/>.
/// </summary>
internal static class PositionEndpoints
{
    public static IEndpointRouteBuilder MapPositionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bff/me/positions", (
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.ListAsync(httpContext.Request.Query, token, ct),
                cancellationToken));

        return endpoints;
    }
}
