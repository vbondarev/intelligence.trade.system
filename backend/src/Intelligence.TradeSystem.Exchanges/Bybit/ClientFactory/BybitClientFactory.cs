using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects;
using BybitNetCredentials = Bybit.Net.BybitCredentials;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

namespace Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

/// <summary>
/// Создаёт изолированные клиенты Bybit для публичных и приватных операций конкретной учётной записи.
/// </summary>
public static class BybitClientFactory
{
    public static IBybitRestClient CreatePublicClient() => new BybitRestClient(static _ => { });

    public static IBybitRestClient CreatePrivateClient(BybitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return new BybitRestClient(options =>
        {
            options.ApiCredentials = new BybitNetCredentials(credentials.ApiKey, credentials.ApiSecret);
            options.RequestTimeout = BybitPrivateResiliencePolicy.RequestTimeout;

            // CryptoExchange.Net по умолчанию повторяет запросы после server rate limits.
            // Retry приватного чтения принадлежит этой boundary, чтобы число попыток при timeout,
            // network-ошибках и rate limits оставалось ограниченным.
            options.RateLimitingBehaviour = RateLimitingBehaviour.Fail;
        });
    }

    /// <summary>
    /// Создаёт websocket-клиент приватных потоков одной учётной записи.
    /// </summary>
    /// <remarks>
    /// Клиент создаётся без <c>ILoggerFactory</c>: при включённом исходном сообщении SDK пишет его
    /// и auth-запрос в trace-лог, а они содержат приватные данные аккаунта и API key.
    /// </remarks>
    public static IBybitSocketClient CreatePrivateSocketClient(BybitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return new BybitSocketClient(options =>
        {
            options.ApiCredentials = new BybitNetCredentials(credentials.ApiKey, credentials.ApiSecret);

            // SDK 7.7.0 берёт временную отметку DataEvent из поля ts, которого нет в приватных
            // сообщениях Bybit; creationTime доступен только в исходном сообщении.
            options.OutputOriginalData = true;
        });
    }
}
