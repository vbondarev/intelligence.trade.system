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

        var response = await send(token.AccessToken!, cancellationToken);
        if (response.StatusCode != StatusCodes.Status401Unauthorized)
        {
            return response;
        }

        // API отклоняет token на authentication boundary до выполнения операции, поэтому даже
        // для state-changing запроса допустим ровно один повтор после принудительного refresh.
        var refreshed = await tokenService.GetAccessTokenAsync(httpContext, token.AccessToken, cancellationToken);
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

    private static ApiForwardResponse? ToFailure(BffAccessTokenResult token) => token.Status switch
    {
        BffAccessTokenStatus.Available => null,
        BffAccessTokenStatus.SessionEnded => ApiForwardResponse.Status(StatusCodes.Status401Unauthorized),
        _ => ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "API повторно отклонил обновлённый access token; BFF session завершена.")]
    private static partial void LogSessionRejectedByApi(ILogger logger);
}
