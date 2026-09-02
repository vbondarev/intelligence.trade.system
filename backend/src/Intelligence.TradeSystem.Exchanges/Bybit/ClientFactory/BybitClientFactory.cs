using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using BybitNetCredentials = Bybit.Net.BybitCredentials;

namespace Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

/// <summary>
/// Creates isolated Bybit REST clients for public and account-specific private operations.
/// </summary>
public static class BybitClientFactory
{
    public static IBybitRestClient CreatePublicClient() => new BybitRestClient(static _ => { });

    public static IBybitRestClient CreatePrivateClient(BybitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return new BybitRestClient(options =>
            options.ApiCredentials = new BybitNetCredentials(credentials.ApiKey, credentials.ApiSecret));
    }
}
