using System.Net.Http.Headers;

namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Явные операции management API подключений текущего пользователя. Upstream path строится
/// только из фиксированного route и <see cref="Guid"/>, поэтому browser не может выбрать
/// произвольный API path.
/// </summary>
/// <remarks>
/// HttpClient этого типа не имеет automatic retry: state-changing запрос после network error,
/// timeout или <c>5xx</c> повторять нельзя. Единственный повтор после <c>401</c> выполняет
/// <see cref="AuthenticatedApiForwarder"/>.
/// </remarks>
internal sealed partial class ExchangeAccountsApiClient(
    HttpClient httpClient,
    ILogger<ExchangeAccountsApiClient> logger)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private const string CollectionPath = "api/v1/me/exchange-accounts";
    private const string JsonMediaType = "application/json";

    public Task<ApiForwardResponse> ListAsync(string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, CollectionPath, null, accessToken, cancellationToken);

    public Task<ApiForwardResponse> CreateAsync(
        byte[] body,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, CollectionPath, body, accessToken, cancellationToken);

    public Task<ApiForwardResponse> RenameAsync(
        Guid id,
        byte[] body,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, ItemPath(id), body, accessToken, cancellationToken);

    public Task<ApiForwardResponse> VerifyAsync(Guid id, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, ItemPath(id) + "/verify", null, accessToken, cancellationToken);

    public Task<ApiForwardResponse> RotateCredentialsAsync(
        Guid id,
        byte[] body,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, ItemPath(id) + "/credentials", body, accessToken, cancellationToken);

    public Task<ApiForwardResponse> DisconnectAsync(Guid id, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, ItemPath(id), null, accessToken, cancellationToken);

    private static string ItemPath(Guid id) => $"{CollectionPath}/{id:D}";

    private async Task<ApiForwardResponse> SendAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(JsonMediaType) { CharSet = "utf-8" };
        }

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return ApiForwardResponse.FromApi(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                responseBody);
        }
        catch (HttpRequestException exception)
        {
            LogApiUnreachable(method.Method, exception.GetType().Name);
            return ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogApiUnreachable(method.Method, exception.GetType().Name);
            return ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "API exchange-accounts недоступен для {Method}: {ExceptionType}.")]
    private partial void LogApiUnreachable(string method, string exceptionType);
}
