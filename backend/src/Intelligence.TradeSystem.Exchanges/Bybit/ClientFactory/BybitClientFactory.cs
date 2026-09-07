using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using CryptoExchange.Net.Objects;
using BybitNetCredentials = Bybit.Net.BybitCredentials;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

namespace Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

/// <summary>
/// Creates isolated Bybit REST clients for public and account-specific private operations.
/// </summary>
public static class BybitClientFactory
{
    public static IBybitRestClient CreatePublicClient() =>
        new BybitRestClient(options =>
        {
            options.RequestTimeout = BybitPrivateResiliencePolicy.RequestTimeout;
        });

    public static IBybitRestClient CreatePrivateClient(BybitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return new BybitRestClient(options =>
        {
            options.ApiCredentials = new BybitNetCredentials(credentials.ApiKey, credentials.ApiSecret);
            options.RequestTimeout = BybitPrivateResiliencePolicy.RequestTimeout;

            // CryptoExchange.Net retries server rate limits by default. Private read retries
            // are owned by this boundary so timeout, network, and rate-limit attempts remain bounded.
            options.RateLimitingBehaviour = RateLimitingBehaviour.Fail;
        });
    }
}
