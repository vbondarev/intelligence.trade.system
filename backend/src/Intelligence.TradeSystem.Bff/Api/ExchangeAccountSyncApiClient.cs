namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Ручная синхронизация подключения текущего пользователя. Upstream path строится только из
/// фиксированного route и <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// HttpClient этого типа не имеет automatic retry: повтор синхронизации после network error,
/// timeout или <c>5xx</c> мог бы повторно обратиться к бирже. Единственный повтор после
/// <c>401</c> выполняет <see cref="AuthenticatedApiForwarder"/>.
/// </remarks>
internal sealed partial class ExchangeAccountSyncApiClient(
    HttpClient httpClient,
    ILogger<ExchangeAccountSyncApiClient> logger)
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public Task<ApiForwardResponse> SyncAsync(Guid exchangeAccountId, string accessToken, CancellationToken cancellationToken) =>
        ApiRequestSender.SendAsync(
            httpClient,
            HttpMethod.Post,
            $"api/v1/exchange-accounts/{exchangeAccountId:D}/sync",
            accessToken,
            LogApiUnreachable,
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API sync недоступен: {ExceptionType}.")]
    private partial void LogApiUnreachable(string exceptionType);
}
