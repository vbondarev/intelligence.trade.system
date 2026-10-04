using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Polly;

namespace Intelligence.TradeSystem.Web.Api;

/// <summary>
/// Вызывает business API с Bearer access token текущей BFF session.
/// </summary>
internal sealed partial class CurrentUserApiClient(
    HttpClient httpClient,
    ILogger<CurrentUserApiClient> logger)
{
    private const string CurrentUserPath = "api/v1/auth/me";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<CurrentUserApiResult> GetCurrentUserAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, CurrentUserPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await ReadCurrentUserAsync(response, cancellationToken);
                case HttpStatusCode.Unauthorized:
                    return CurrentUserApiResult.Unauthorized;
                case HttpStatusCode.Forbidden:
                    return CurrentUserApiResult.Forbidden;
                default:
                    LogApiFailed((int)response.StatusCode);
                    return CurrentUserApiResult.Unavailable;
            }
        }
        catch (HttpRequestException exception)
        {
            LogApiUnreachable(exception.GetType().Name);
            return CurrentUserApiResult.Unavailable;
        }
        catch (ExecutionRejectedException exception)
        {
            LogApiUnreachable(exception.GetType().Name);
            return CurrentUserApiResult.Unavailable;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiUnreachable(exception.GetType().Name);
            return CurrentUserApiResult.Unavailable;
        }
    }

    private async Task<CurrentUserApiResult> ReadCurrentUserAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        CurrentUserApiResponse? currentUser;
        try
        {
            currentUser = await response.Content.ReadFromJsonAsync<CurrentUserApiResponse>(
                SerializerOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            currentUser = null;
        }

        if (currentUser is { Authenticated: true }
            && currentUser.UserId != Guid.Empty
            && !string.IsNullOrWhiteSpace(currentUser.Subject))
        {
            return CurrentUserApiResult.Success(currentUser);
        }

        LogMalformedResponse();
        return CurrentUserApiResult.Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API current-user вернул статус {StatusCode}.")]
    private partial void LogApiFailed(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API current-user недоступен: {ExceptionType}.")]
    private partial void LogApiUnreachable(string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API current-user вернул некорректный ответ.")]
    private partial void LogMalformedResponse();
}

internal enum CurrentUserApiStatus
{
    Success,
    Unauthorized,
    Forbidden,
    Unavailable,
}

internal sealed class CurrentUserApiResult
{
    public static readonly CurrentUserApiResult Unauthorized = new(CurrentUserApiStatus.Unauthorized, null);
    public static readonly CurrentUserApiResult Forbidden = new(CurrentUserApiStatus.Forbidden, null);
    public static readonly CurrentUserApiResult Unavailable = new(CurrentUserApiStatus.Unavailable, null);

    private CurrentUserApiResult(CurrentUserApiStatus status, CurrentUserApiResponse? user)
    {
        Status = status;
        User = user;
    }

    public CurrentUserApiStatus Status { get; }

    public CurrentUserApiResponse? User { get; }

    public static CurrentUserApiResult Success(CurrentUserApiResponse user) =>
        new(CurrentUserApiStatus.Success, user);
}
