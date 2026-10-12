using Intelligence.TradeSystem.Bff.Api;

namespace Intelligence.TradeSystem.Bff.Endpoints;

/// <summary>
/// Browser-facing операции с позициями текущего пользователя. Каждый endpoint соответствует одной
/// заранее определённой операции API; query ограничен allowlist <see cref="PositionsApiClient"/>.
/// </summary>
/// <remarks>
/// Явная оценка — state-changing <c>POST</c> без request body, поэтому её защищает общий
/// antiforgery middleware BFF до обращения к API.
/// </remarks>
internal static class PositionEndpoints
{
    public static IEndpointRouteBuilder MapPositionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var positions = endpoints.MapGroup("/bff/me/positions");

        positions.MapGet("", (
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.ListAsync(httpContext.Request.Query, token, ct),
                cancellationToken));

        positions.MapGet("/{id:guid}", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetAsync(id, token, ct),
                cancellationToken));

        positions.MapGet("/{id:guid}/market", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetMarketAsync(id, token, ct),
                cancellationToken));

        positions.MapGet("/{id:guid}/candles", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetCandlesAsync(id, httpContext.Request.Query, token, ct),
                cancellationToken));

        positions.MapGet("/{id:guid}/evaluation", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetEvaluationAsync(id, token, ct),
                cancellationToken));

        positions.MapGet("/{id:guid}/timeline", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetTimelineAsync(id, httpContext.Request.Query, token, ct),
                cancellationToken));

        positions.MapPost("/{id:guid}/evaluation", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PositionsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.EvaluateAsync(id, token, ct),
                cancellationToken));

        return endpoints;
    }
}
