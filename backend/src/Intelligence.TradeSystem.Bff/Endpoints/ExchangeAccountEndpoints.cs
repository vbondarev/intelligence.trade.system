using Intelligence.TradeSystem.Bff.Api;

namespace Intelligence.TradeSystem.Bff.Endpoints;

/// <summary>
/// Browser-facing management API подключений текущего пользователя. Каждый endpoint
/// соответствует одной заранее определённой операции API; generic proxy отсутствует.
/// Ручная синхронизация через BFF не публикуется.
/// </summary>
internal static class ExchangeAccountEndpoints
{
    public static IEndpointRouteBuilder MapExchangeAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/bff/me/exchange-accounts");

        group.MapGet("", (
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(httpContext, apiClient.ListAsync, cancellationToken));

        group.MapPost("", (
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardWithBodyAsync(
                httpContext,
                (body, token, ct) => apiClient.CreateAsync(body, token, ct),
                cancellationToken));

        group.MapPatch("/{id:guid}", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardWithBodyAsync(
                httpContext,
                (body, token, ct) => apiClient.RenameAsync(id, body, token, ct),
                cancellationToken));

        group.MapPost("/{id:guid}/verify", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.VerifyAsync(id, token, ct),
                cancellationToken));

        group.MapPut("/{id:guid}/credentials", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardWithBodyAsync(
                httpContext,
                (body, token, ct) => apiClient.RotateCredentialsAsync(id, body, token, ct),
                cancellationToken));

        group.MapDelete("/{id:guid}", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            forwarder.ForwardAsync(
                httpContext,
                (token, ct) => apiClient.DisconnectAsync(id, token, ct),
                cancellationToken));

        return endpoints;
    }
}
