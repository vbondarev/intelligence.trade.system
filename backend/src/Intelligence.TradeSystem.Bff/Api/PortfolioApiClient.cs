namespace Intelligence.TradeSystem.Bff.Api;

/// <summary>
/// Чтение сводки портфеля подключения текущего пользователя. Upstream path строится только из
/// фиксированного route и <see cref="Guid"/>.
/// </summary>
internal sealed partial class PortfolioApiClient(
    HttpClient httpClient,
    ILogger<PortfolioApiClient> logger)
{
    public Task<ApiForwardResponse> GetAsync(Guid exchangeAccountId, string accessToken, CancellationToken cancellationToken) =>
        ApiRequestSender.SendAsync(
            httpClient,
            HttpMethod.Get,
            $"api/v1/exchange-accounts/{exchangeAccountId:D}/portfolio",
            accessToken,
            LogApiUnreachable,
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Warning, Message = "API portfolio недоступен: {ExceptionType}.")]
    private partial void LogApiUnreachable(string exceptionType);
}
