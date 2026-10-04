using System.Net;
using System.Text.Json;
using Intelligence.TradeSystem.Bff.Configuration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Bff.Authentication;

/// <summary>
/// Выполняет refresh_token grant confidential client BFF. Refresh token может
/// ротироваться при каждом использовании, поэтому HTTP client не имеет automatic retry.
/// </summary>
internal sealed partial class OAuthTokenRefreshClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<OpenIdConnectOptions> openIdConnectOptions,
    BffOidcSettings oidcSettings,
    ILogger<OAuthTokenRefreshClient> logger)
{
    public const string HttpClientName = "OidcToken";

    public async Task<OAuthTokenRefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        var tokenEndpoint = await GetTokenEndpointAsync(cancellationToken);
        if (tokenEndpoint is null)
        {
            return OAuthTokenRefreshResult.Unavailable;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("client_id", oidcSettings.ClientId),
                new KeyValuePair<string, string>("client_secret", oidcSettings.ClientSecret),
            ]),
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogTokenEndpointUnreachable(exception.GetType().Name);
            return OAuthTokenRefreshResult.Unavailable;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTokenEndpointUnreachable(exception.GetType().Name);
            return OAuthTokenRefreshResult.Unavailable;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await ReadTokenResponseAsync(response, cancellationToken);
            }

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                var error = await ReadErrorCodeAsync(response, cancellationToken);
                if (string.Equals(error, "invalid_grant", StringComparison.Ordinal))
                {
                    LogRefreshRejected();
                    return OAuthTokenRefreshResult.Rejected;
                }

                LogRefreshFailed((int)response.StatusCode, error ?? "unknown");
                return OAuthTokenRefreshResult.Unavailable;
            }

            LogRefreshFailed((int)response.StatusCode, "unknown");
            return OAuthTokenRefreshResult.Unavailable;
        }
    }

    private async Task<string?> GetTokenEndpointAsync(CancellationToken cancellationToken)
    {
        var options = openIdConnectOptions.Get(BffAuthenticationExtensions.OidcScheme);
        if (options.ConfigurationManager is null)
        {
            LogDiscoveryUnavailable("ConfigurationManagerMissing");
            return null;
        }

        try
        {
            var configuration = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(configuration.TokenEndpoint))
            {
                return configuration.TokenEndpoint;
            }

            LogDiscoveryUnavailable("TokenEndpointMissing");
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDiscoveryUnavailable(exception.GetType().Name);
            return null;
        }
    }

    private async Task<OAuthTokenRefreshResult> ReadTokenResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            var accessToken = GetString(root, "access_token");
            if (string.IsNullOrEmpty(accessToken)
                || !root.TryGetProperty("expires_in", out var expiresInElement)
                || expiresInElement.ValueKind != JsonValueKind.Number
                || !expiresInElement.TryGetInt64(out var expiresInSeconds)
                || expiresInSeconds <= 0)
            {
                LogMalformedTokenResponse();
                return OAuthTokenRefreshResult.Unavailable;
            }

            return OAuthTokenRefreshResult.Succeeded(
                accessToken,
                TimeSpan.FromSeconds(expiresInSeconds),
                GetString(root, "refresh_token"),
                GetString(root, "id_token"));
        }
        catch (JsonException)
        {
            LogMalformedTokenResponse();
            return OAuthTokenRefreshResult.Unavailable;
        }
    }

    private static async Task<string?> ReadErrorCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return GetString(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC discovery недоступен для refresh: {Reason}.")]
    private partial void LogDiscoveryUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC token endpoint недоступен для refresh: {ExceptionType}.")]
    private partial void LogTokenEndpointUnreachable(string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "OIDC token endpoint отклонил refresh token: invalid_grant.")]
    private partial void LogRefreshRejected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC refresh завершился ошибкой {StatusCode} ({Error}).")]
    private partial void LogRefreshFailed(int statusCode, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC token endpoint вернул некорректный ответ на refresh.")]
    private partial void LogMalformedTokenResponse();
}

internal enum OAuthTokenRefreshStatus
{
    Succeeded,
    Rejected,
    Unavailable,
}

/// <summary>
/// Результат refresh_token grant. Тип не является record, чтобы tokens не попадали
/// в автоматический <c>ToString</c>.
/// </summary>
internal sealed class OAuthTokenRefreshResult
{
    public static readonly OAuthTokenRefreshResult Rejected = new(OAuthTokenRefreshStatus.Rejected);
    public static readonly OAuthTokenRefreshResult Unavailable = new(OAuthTokenRefreshStatus.Unavailable);

    private OAuthTokenRefreshResult(OAuthTokenRefreshStatus status)
    {
        Status = status;
    }

    public OAuthTokenRefreshStatus Status { get; }

    public string? AccessToken { get; private init; }

    public TimeSpan ExpiresIn { get; private init; }

    public string? RefreshToken { get; private init; }

    public string? IdToken { get; private init; }

    public static OAuthTokenRefreshResult Succeeded(
        string accessToken,
        TimeSpan expiresIn,
        string? refreshToken,
        string? idToken) =>
        new(OAuthTokenRefreshStatus.Succeeded)
        {
            AccessToken = accessToken,
            ExpiresIn = expiresIn,
            RefreshToken = refreshToken,
            IdToken = idToken,
        };
}
