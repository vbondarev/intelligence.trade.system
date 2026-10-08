using Intelligence.TradeSystem.Bff.Api;

namespace Intelligence.TradeSystem.Bff.Endpoints;

/// <summary>
/// Browser-facing чтение сводки портфеля и ручная синхронизация выбранного подключения.
/// Каждый endpoint соответствует одной заранее определённой операции API.
/// </summary>
/// <remarks>
/// Синхронизация — state-changing <c>POST</c>, поэтому её защищает общий antiforgery middleware
/// BFF до обращения к API.
/// </remarks>
internal static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/me/exchange-accounts");

        group.MapGet("/{id:guid}/portfolio", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                PortfolioApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.GetAsync(id, token, ct),
                cancellationToken));

        group.MapPost("/{id:guid}/sync", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountSyncApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.SyncAsync(id, token, ct),
                cancellationToken));

        return endpoints;
    }
}
