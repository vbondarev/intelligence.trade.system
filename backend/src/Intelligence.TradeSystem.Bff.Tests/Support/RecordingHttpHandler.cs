using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Fake upstream (Identity token endpoint или business API), который запоминает запросы
/// до того, как HttpClient освободит их содержимое.
/// </summary>
internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<RecordedRequest> requests = new();

    public Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> Responder { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

    public IReadOnlyList<RecordedRequest> Requests => [.. requests];

    public int Count => requests.Count;

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body);
        requests.Enqueue(recorded);

        return await Responder(recorded, cancellationToken);
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri RequestUri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string Body)
{
    public IReadOnlyDictionary<string, string> Form =>
        Body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty,
                StringComparer.Ordinal);
}
