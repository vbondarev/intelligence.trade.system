using System.Globalization;
using Intelligence.TradeSystem.Web.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;

namespace Intelligence.TradeSystem.Web.Authentication;

/// <summary>
/// Выдаёт актуальный access token текущей BFF session и обновляет его через refresh token.
/// Tokens не покидают server-side ticket и не возвращаются browser.
/// </summary>
internal sealed partial class BffTokenService(
    ITicketStore ticketStore,
    OAuthTokenRefreshClient refreshClient,
    IMemoryCache cache,
    WebSessionOptions sessionOptions,
    TimeProvider timeProvider,
    ILogger<BffTokenService> logger)
{
    internal static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);

    private const string AccessTokenName = "access_token";
    private const string RefreshTokenName = "refresh_token";
    private const string IdTokenName = "id_token";
    private const string ExpiresAtName = "expires_at";
    private const string SubjectClaim = "sub";
    private const string RefreshLockCacheKeyPrefix = "bff-refresh-lock:";

    private readonly Lock refreshLockGate = new();

    /// <summary>
    /// Возвращает access token, пригодный для вызова API.
    /// </summary>
    /// <param name="httpContext">Текущий browser request с BFF session cookie.</param>
    /// <param name="rejectedAccessToken">
    /// Token, отклонённый API с 401. Если session всё ещё содержит именно его, выполняется
    /// принудительный refresh; если token уже обновил параллельный запрос, возвращается новый.
    /// </param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    public async Task<BffAccessTokenResult> GetAccessTokenAsync(
        HttpContext httpContext,
        string? rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        var authentication = await httpContext.AuthenticateAsync(WebAuthenticationExtensions.SessionScheme);
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

        var refreshLock = GetRefreshLock(subject);
        await refreshLock.WaitAsync(cancellationToken);
        try
        {
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

            var refreshToken = current.Properties.GetTokenValue(RefreshTokenName);
            if (string.IsNullOrEmpty(refreshToken))
            {
                LogRefreshTokenMissing();
                await EndSessionAsync(httpContext);
                return BffAccessTokenResult.SessionEnded;
            }

            var refresh = await refreshClient.RefreshAsync(refreshToken, cancellationToken);
            switch (refresh.Status)
            {
                case OAuthTokenRefreshStatus.Rejected:
                    await EndSessionAsync(httpContext);
                    return BffAccessTokenResult.SessionEnded;
                case OAuthTokenRefreshStatus.Unavailable:
                    return BffAccessTokenResult.TemporarilyUnavailable;
            }

            // Tokens сохраняются в существующий server-side ticket без нового sign-in: если
            // параллельный logout уже удалил session, RenewAsync её не восстановит.
            ApplyRefreshedTokens(current.Properties, refresh);
            await ticketStore.RenewAsync(sessionKey, current, httpContext, cancellationToken);
            if (await ticketStore.RetrieveAsync(sessionKey, httpContext, cancellationToken) is null)
            {
                await EndSessionAsync(httpContext);
                return BffAccessTokenResult.SessionEnded;
            }

            return BffAccessTokenResult.Available(refresh.AccessToken!);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private SemaphoreSlim GetRefreshLock(string subject)
    {
        // GetOrCreate не атомарен: без gate параллельные запросы могли бы получить разные
        // semaphore и выполнить два refresh одного ротируемого refresh token.
        lock (refreshLockGate)
        {
            return cache.GetOrCreate(
                RefreshLockCacheKeyPrefix + subject,
                entry =>
                {
                    // Sliding lifetime session не даёт вытеснить semaphore, пока session активна,
                    // а refresh удерживает его не дольше timeout token endpoint.
                    entry.SlidingExpiration = sessionOptions.Lifetime;
                    return new SemaphoreSlim(1, 1);
                })!;
        }
    }

    private void ApplyRefreshedTokens(AuthenticationProperties properties, OAuthTokenRefreshResult refresh)
    {
        var tokens = properties.GetTokens().ToDictionary(token => token.Name, token => token.Value, StringComparer.Ordinal);
        tokens[AccessTokenName] = refresh.AccessToken!;
        tokens[ExpiresAtName] = timeProvider.GetUtcNow()
            .Add(refresh.ExpiresIn)
            .ToString("o", CultureInfo.InvariantCulture);

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
        httpContext.SignOutAsync(WebAuthenticationExtensions.SessionScheme);

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
