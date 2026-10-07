using Microsoft.Extensions.Primitives;

namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Чтение списка позиций текущего пользователя. Upstream path фиксирован; из browser query
/// передаются только параметры allowlist, без интерпретации значений.
/// </summary>
/// <remarks>
/// Значения, включая opaque cursor, передаются как есть и URL-encoded: валидацию и фильтрацию
/// выполняет API. Неизвестные параметры, в том числе <c>userId</c>, отбрасываются.
/// </remarks>
internal sealed partial class PositionsApiClient(
    HttpClient httpClient,
    ILogger<PositionsApiClient> logger)
{
    private const string CollectionPath = "api/v1/positions";

    private static readonly string[] AllowedQueryParameters =
    [
        "exchangeAccountId",
        "trackingState",
        "symbol",
        "side",
        "pageSize",
        "cursor",
    ];

    public Task<ApiForwardResponse> ListAsync(
        IQueryCollection browserQuery,
        string accessToken,
        CancellationToken cancellationToken) =>
        ApiRequestSender.SendAsync(
            httpClient,
            HttpMethod.Get,
            CollectionPath + BuildQueryString(browserQuery),
            accessToken,
            LogApiUnreachable,
            cancellationToken);

    internal static string BuildQueryString(IQueryCollection browserQuery)
    {
        var parameters = new List<KeyValuePair<string, string?>>();
        foreach (var name in AllowedQueryParameters)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "API positions недоступен: {ExceptionType}.")]
    private partial void LogApiUnreachable(string exceptionType);
}
