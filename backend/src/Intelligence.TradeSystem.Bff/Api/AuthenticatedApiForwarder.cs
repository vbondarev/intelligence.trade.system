using System.Buffers;
using System.Security.Cryptography;
using Intelligence.TradeSystem.Bff.Authentication;
using Microsoft.AspNetCore.Authentication;

namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Выполняет вызов business API с access token текущей BFF session. Tokens не попадают
/// в ответ browser.
/// </summary>
internal sealed partial class AuthenticatedApiForwarder(
    BffTokenService tokenService,
    ILogger<AuthenticatedApiForwarder> logger)
{
    private const int BodyReadChunkSize = 4096;

    public async Task<ApiForwardResponse> ForwardAsync(
        HttpContext httpContext,
        Func<string, CancellationToken, Task<ApiForwardResponse>> send,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(httpContext, null, cancellationToken);
        if (ToFailure(token) is { } tokenFailure)
        {
            return tokenFailure;
        }

        return await SendWithSingleRetryAsync(httpContext, token.AccessToken!, send, cancellationToken);
    }

    /// <summary>
    /// Выполняет JSON mutation, тело которой может содержать API key/secret. Body читается только
    /// после получения usable access token: без session или при недоступном token service
    /// BFF не читает и не буферизует credentials. Буфер нужен для повтора после принудительного
    /// refresh; он не логируется, не сохраняется и очищается после завершения операции.
    /// </summary>
    public async Task<ApiForwardResponse> ForwardWithBodyAsync(
        HttpContext httpContext,
        Func<byte[], string, CancellationToken, Task<ApiForwardResponse>> send,
        CancellationToken cancellationToken)
    {
        var token = await tokenService.GetAccessTokenAsync(httpContext, null, cancellationToken);
        if (ToFailure(token) is { } tokenFailure)
        {
            return tokenFailure;
        }

        var body = await ReadSensitiveBodyAsync(httpContext.Request.Body, cancellationToken);
        try
        {
            return await SendWithSingleRetryAsync(
                httpContext,
                token.AccessToken!,
                (accessToken, ct) => send(body, accessToken, ct),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(body);
        }
    }

    private async Task<ApiForwardResponse> SendWithSingleRetryAsync(
        HttpContext httpContext,
        string accessToken,
        Func<string, CancellationToken, Task<ApiForwardResponse>> send,
        CancellationToken cancellationToken)
    {
        var response = await send(accessToken, cancellationToken);
        if (response.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return response;
        }

        // API отклоняет token на authentication boundary до выполнения операции, поэтому даже
        // для state-changing запроса допустим ровно один повтор после принудительного refresh.
        var refreshed = await tokenService.GetAccessTokenAsync(httpContext, accessToken, cancellationToken);
        if (ToFailure(refreshed) is { } refreshFailure)
        {
            return refreshFailure;
        }

        response = await send(refreshed.AccessToken!, cancellationToken);
        if (response.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return response;
        }

        LogSessionRejectedByApi(logger);
        await httpContext.SignOutAsync(BffAuthenticationExtensions.SessionScheme);
        return ApiForwardResponse.Status(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// Читает body в собственный буфер вместо <see cref="Stream.CopyToAsync(Stream)"/>: при росте
    /// буфера и при exception/cancellation каждый промежуточный массив очищается, а не остаётся
    /// в памяти или в общем pool с фрагментами credentials.
    /// </summary>
    private static async Task<byte[]> ReadSensitiveBodyAsync(Stream body, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BodyReadChunkSize);
        var length = 0;
        try
        {
            while (true)
            {
                if (length == buffer.Length)
                {
                    var larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, length).CopyTo(larger);
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    buffer = larger;
                }

                var read = await body.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    return buffer.AsSpan(0, length).ToArray();
                }

                length += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static ApiForwardResponse? ToFailure(BffAccessTokenResult token) => token.Status switch
    {
        BffAccessTokenStatus.Available => null,
        BffAccessTokenStatus.SessionEnded => ApiForwardResponse.Status(StatusCodes.Status401Unauthorized),
        _ => ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "API повторно отклонил обновлённый access token; BFF session завершена.")]
    private static partial void LogSessionRejectedByApi(ILogger logger);
}
