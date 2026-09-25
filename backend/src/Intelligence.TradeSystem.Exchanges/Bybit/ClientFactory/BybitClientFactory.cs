using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects;
using BybitNetCredentials = Bybit.Net.BybitCredentials;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

namespace Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

/// <summary>
/// Создаёт изолированные REST-клиенты Bybit для публичных и приватных операций конкретной учётной записи.
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
}
