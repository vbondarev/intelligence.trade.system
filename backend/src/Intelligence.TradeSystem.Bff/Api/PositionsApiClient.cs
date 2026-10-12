using Microsoft.Extensions.Primitives;

namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Чтение позиций текущего пользователя, их рыночного контекста, свечей, оценки и истории, а также явный
/// запуск оценки. Upstream paths фиксированы и строятся только из route и <see cref="Guid"/>;
/// из browser query передаются только параметры allowlist, без интерпретации значений.
/// </summary>
/// <remarks>
/// Значения, включая opaque cursor, передаются как есть и URL-encoded: валидацию и фильтрацию
/// выполняет API. Неизвестные параметры, в том числе <c>userId</c>, отбрасываются.
/// HttpClient этого типа не имеет automatic retry: повтор оценки после network error, timeout
/// или <c>5xx</c> мог бы повторно выполнить уже применённую операцию. Единственный повтор после
/// <c>401</c> выполняет <see cref="AuthenticatedApiForwarder"/>.
/// </remarks>
internal sealed partial class PositionsApiClient(
    HttpClient httpClient,
    ILogger<PositionsApiClient> logger)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private const string CollectionPath = "api/v1/positions";

    private static readonly string[] AllowedListQueryParameters =
    [
        "exchangeAccountId",
        "trackingState",
        "symbol",
        "side",
        "pageSize",
        "cursor",
    ];

    private static readonly string[] AllowedCandlesQueryParameters =
    [
        "interval",
        "limit",
    ];

    private static readonly string[] AllowedTimelineQueryParameters =
    [
        "pageSize",
        "type",
        "cursor",
    ];

    public Task<ApiForwardResponse> ListAsync(
        IQueryCollection browserQuery,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Get,
            CollectionPath + BuildQueryString(browserQuery),
            accessToken,
            cancellationToken);

    public Task<ApiForwardResponse> GetAsync(Guid positionId, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, PositionPath(positionId), accessToken, cancellationToken);

    public Task<ApiForwardResponse> GetMarketAsync(Guid positionId, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, PositionPath(positionId) + "/market", accessToken, cancellationToken);

    public Task<ApiForwardResponse> GetCandlesAsync(
        Guid positionId,
        IQueryCollection browserQuery,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Get,
            PositionPath(positionId) + "/candles" + BuildCandlesQueryString(browserQuery),
            accessToken,
            cancellationToken);

    public Task<ApiForwardResponse> GetEvaluationAsync(Guid positionId, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, PositionPath(positionId) + "/evaluation", accessToken, cancellationToken);

    public Task<ApiForwardResponse> GetTimelineAsync(
        Guid positionId,
        IQueryCollection browserQuery,
        string accessToken,
        CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Get,
            PositionPath(positionId) + "/timeline" + BuildTimelineQueryString(browserQuery),
            accessToken,
            cancellationToken);

    public Task<ApiForwardResponse> EvaluateAsync(Guid positionId, string accessToken, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, PositionPath(positionId) + "/evaluation", accessToken, cancellationToken);

    internal static string BuildQueryString(IQueryCollection browserQuery) =>
        BuildQueryString(browserQuery, AllowedListQueryParameters);

    internal static string BuildCandlesQueryString(IQueryCollection browserQuery) =>
        BuildQueryString(browserQuery, AllowedCandlesQueryParameters);

    internal static string BuildTimelineQueryString(IQueryCollection browserQuery) =>
        BuildQueryString(browserQuery, AllowedTimelineQueryParameters);

    private static string PositionPath(Guid positionId) => $"{CollectionPath}/{positionId:D}";

    private static string BuildQueryString(IQueryCollection browserQuery, string[] allowedParameters)
    {
        var parameters = new List<KeyValuePair<string, string?>>();
        foreach (var name in allowedParameters)
        {
            if (!browserQuery.TryGetValue(name, out StringValues values))
            {
                continue;
            }

            foreach (var value in values)
            {
                parameters.Add(new KeyValuePair<string, string?>(name, value ?? string.Empty));
            }
        }

        return QueryString.Create(parameters).ToUriComponent();
    }

    private Task<ApiForwardResponse> SendAsync(
        HttpMethod method,
        string path,
        string accessToken,
        CancellationToken cancellationToken) =>
        ApiRequestSender.SendAsync(httpClient, method, path, accessToken, LogApiUnreachable, cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API positions недоступен: {ExceptionType}.")]
    private partial void LogApiUnreachable(string exceptionType);
}
