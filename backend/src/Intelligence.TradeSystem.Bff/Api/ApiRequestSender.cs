using System.Net.Http.Headers;
using Polly;

namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Отправляет в business API запрос без body с Bearer token и преобразует ответ в
/// <see cref="ApiForwardResponse"/>. Недоступность API возвращается browser как <c>503</c>
/// без текста исключения.
/// </summary>
internal static class ApiRequestSender
{
    private const string JsonMediaType = "application/json";

    public static async Task<ApiForwardResponse> SendAsync(
        HttpClient httpClient,
        HttpMethod method,
        string pathAndQuery,
        string accessToken,
        Action<string> logUnreachable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);

        using var request = new HttpRequestMessage(method, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMediaType));

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
            logUnreachable(exception.GetType().Name);
            return ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable);
        }
        catch (ExecutionRejectedException exception)
        {
            logUnreachable(exception.GetType().Name);
            return ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logUnreachable(exception.GetType().Name);
            return ApiForwardResponse.Status(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
