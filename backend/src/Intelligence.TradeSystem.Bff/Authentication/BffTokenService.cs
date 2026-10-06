using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Hosting;

namespace Intelligence.TradeSystem.Bff.Authentication;

/// <summary>
/// Выдаёт актуальный access token текущей BFF session и обновляет его через refresh token.
/// Tokens не покидают server-side ticket и не возвращаются browser.
/// </summary>
internal sealed partial class BffTokenService(
    ITicketStore ticketStore,
    OAuthTokenRefreshClient refreshClient,
    RefreshGateRegistry refreshGates,
    TimeProvider timeProvider,
    IHostApplicationLifetime applicationLifetime,
    ILogger<BffTokenService> logger)
{
    private const string AccessTokenName = "access_token";
    private const string RefreshTokenName = "refresh_token";
    private const string IdTokenName = "id_token";
    private const string ExpiresAtName = "expires_at";
    private const string SubjectClaim = "sub";

    /// <summary>
    /// Access token используется, только если до истечения остаётся больше этой величины.
    /// Порог — внутренний invariant, а не deployment setting.
    /// </summary>
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Возвращает access token, пригодный для вызова API.
    /// </summary>
    /// <param name="httpContext">Текущий browser request с BFF session cookie.</param>
    /// <param name="rejectedAccessToken">
    /// Token, отклонённый API с 401. Если session всё ещё содержит именно его, выполняется
    /// принудительный refresh; если token уже обновил параллельный запрос, возвращается новый.
    /// </param>
    /// <param name="cancellationToken">Токен отмены запроса. После начала refresh grant не используется.</param>
    public async Task<BffAccessTokenResult> GetAccessTokenAsync(
        HttpContext httpContext,
        string? rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(BffAuthenticationExtensions.SessionScheme);
        if (!authentication.Succeeded || authentication.Ticket is null)
        {
            return BffAccessTokenResult.SessionEnded;
        }

        var subject = authentication.Principal.FindFirst(SubjectClaim)?.Value;
        if (string.IsNullOrEmpty(subject)
            || !authentication.Ticket.Properties.Items.TryGetValue(
                InMemoryAuthenticationTicketStore.SessionKeyItem,
                out var sessionKey)
            || string.IsNullOrEmpty(sessionKey))
        {
            await EndSessionAsync(httpContext);
            return BffAccessTokenResult.SessionEnded;
        }

        if (rejectedAccessToken is null
            && TryGetUsableAccessToken(authentication.Ticket.Properties, out var cachedAccessToken))
        {
            return BffAccessTokenResult.Available(cachedAccessToken);
        }

        var refreshLock = refreshGates.Acquire(subject);
        var refreshLockAcquired = false;
        try
        {
            await refreshLock.Semaphore.WaitAsync(cancellationToken);
            refreshLockAcquired = true;

            // Параллельный запрос той же session мог уже обновить или завершить её, пока этот ждал lock.
            var current = await ticketStore.RetrieveAsync(sessionKey, httpContext, cancellationToken);
            if (current is null)
            {
                await EndSessionAsync(httpContext);
                return BffAccessTokenResult.SessionEnded;
            }

            var currentAccessToken = current.Properties.GetTokenValue(AccessTokenName);
            var rejectedTokenIsCurrent = rejectedAccessToken is not null
                && string.Equals(currentAccessToken, rejectedAccessToken, StringComparison.Ordinal);
            if (!rejectedTokenIsCurrent && TryGetUsableAccessToken(current.Properties, out var freshAccessToken))
            {
                return BffAccessTokenResult.Available(freshAccessToken);
            }

            if (refreshLock.RefreshUnavailable)
            {
                return BffAccessTokenResult.TemporarilyUnavailable;
            }

            var refreshToken = current.Properties.GetTokenValue(RefreshTokenName);
            if (string.IsNullOrEmpty(refreshToken))
            {
                LogRefreshTokenMissing();
                await EndSessionAsync(httpContext);
                return BffAccessTokenResult.SessionEnded;
            }

            // До grant отмена browser request ещё допустима. Дальше lifetime принадлежит BFF:
            // rotated refresh token нужно записать, даже если browser уже отключился.
            cancellationToken.ThrowIfCancellationRequested();
            var refresh = await refreshClient.RefreshAsync(refreshToken, cancellationToken);
            var persistenceCancellation = applicationLifetime.ApplicationStopping;
            switch (refresh.Status)
            {
                case OAuthTokenRefreshStatus.Rejected:
                    await EndSessionAsync(httpContext);
                    return BffAccessTokenResult.SessionEnded;
                case OAuthTokenRefreshStatus.Unavailable:
                    refreshLock.RefreshUnavailable = true;
                    return BffAccessTokenResult.TemporarilyUnavailable;
            }

            // Tokens сохраняются в существующий server-side ticket без нового sign-in: если
            // параллельный logout уже удалил session, RenewAsync её не восстановит.
            ApplyRefreshedTokens(current.Properties, refresh);
            await ticketStore.RenewAsync(sessionKey, current, httpContext, persistenceCancellation);
            if (await ticketStore.RetrieveAsync(sessionKey, httpContext, persistenceCancellation) is null)
            {
                await EndSessionAsync(httpContext);
                return BffAccessTokenResult.SessionEnded;
            }

            return BffAccessTokenResult.Available(refresh.AccessToken!);
        }
        finally
        {
            if (refreshLockAcquired)
            {
                refreshLock.Semaphore.Release();
            }

            refreshGates.Release(subject, refreshLock);
        }
    }

    private static void ApplyRefreshedTokens(AuthenticationProperties properties, OAuthTokenRefreshResult refresh)
    {
        var tokens = properties.GetTokens().ToDictionary(token => token.Name, token => token.Value, StringComparer.Ordinal);
        tokens[AccessTokenName] = refresh.AccessToken!;
        tokens[ExpiresAtName] = refresh.ExpiresAt.ToString("o", CultureInfo.InvariantCulture);

        if (!string.IsNullOrEmpty(refresh.RefreshToken))
        {
            tokens[RefreshTokenName] = refresh.RefreshToken;
        }

        if (!string.IsNullOrEmpty(refresh.IdToken))
        {
            tokens[IdTokenName] = refresh.IdToken;
        }

        properties.StoreTokens(tokens.Select(token => new AuthenticationToken
        {
            Name = token.Key,
            Value = token.Value,
        }));
        InMemoryAuthenticationTicketStore.SetTokenGeneration(
            properties,
            InMemoryAuthenticationTicketStore.GetTokenGeneration(properties) + 1);
    }

    private bool TryGetUsableAccessToken(AuthenticationProperties properties, out string accessToken)
    {
        accessToken = properties.GetTokenValue(AccessTokenName) ?? string.Empty;
        var expiresAt = properties.GetTokenValue(ExpiresAtName);

        return accessToken.Length > 0
            && DateTimeOffset.TryParse(
                expiresAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var expiresAtValue)
            && expiresAtValue > timeProvider.GetUtcNow().Add(RefreshSkew);
    }

    private static Task EndSessionAsync(HttpContext httpContext) =>
        httpContext.SignOutAsync(BffAuthenticationExtensions.SessionScheme);

    [LoggerMessage(Level = LogLevel.Warning, Message = "BFF session не содержит refresh token и будет завершена.")]
    private partial void LogRefreshTokenMissing();
}

internal enum BffAccessTokenStatus
{
    Available,
    SessionEnded,
    TemporarilyUnavailable,
}

/// <summary>
/// Результат получения access token. Тип не является record, чтобы token не попадал
/// в автоматический <c>ToString</c>.
/// </summary>
internal sealed class BffAccessTokenResult
{
    public static readonly BffAccessTokenResult SessionEnded = new(BffAccessTokenStatus.SessionEnded, null);
    public static readonly BffAccessTokenResult TemporarilyUnavailable =
        new(BffAccessTokenStatus.TemporarilyUnavailable, null);

    private BffAccessTokenResult(BffAccessTokenStatus status, string? accessToken)
    {
        Status = status;
        AccessToken = accessToken;
    }

    public BffAccessTokenStatus Status { get; }

    public string? AccessToken { get; }

    public static BffAccessTokenResult Available(string accessToken) =>
        new(BffAccessTokenStatus.Available, accessToken);
}
