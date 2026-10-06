using System.Security.Cryptography;
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
            ForwardWithBodyAsync(
                httpContext,
                forwarder,
                (body, token, ct) => apiClient.CreateAsync(body, token, ct),
                cancellationToken));

        group.MapPatch("/{id:guid}", (
                Guid id,
                HttpContext httpContext,
                AuthenticatedApiForwarder forwarder,
                ExchangeAccountsApiClient apiClient,
                CancellationToken cancellationToken) =>
            ForwardWithBodyAsync(
                httpContext,
                forwarder,
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
            ForwardWithBodyAsync(
                httpContext,
                forwarder,
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

    /// <summary>
    /// Буферизует JSON body в памяти, чтобы его можно было отправить повторно после
    /// принудительного refresh. Body может содержать API key/secret: он не логируется,
    /// не сохраняется и очищается сразу после завершения операции.
    /// </summary>
    private static async Task<ApiForwardResponse> ForwardWithBodyAsync(
        HttpContext httpContext,
        AuthenticatedApiForwarder forwarder,
        Func<byte[], string, CancellationToken, Task<ApiForwardResponse>> send,
        CancellationToken cancellationToken)
    {
        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await httpContext.Request.Body.CopyToAsync(buffer, cancellationToken);
            body = buffer.ToArray();
            CryptographicOperations.ZeroMemory(buffer.GetBuffer());
        }

        try
        {
            return await forwarder.ForwardAsync(
                httpContext,
                (token, ct) => send(body, token, ct),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }
}
