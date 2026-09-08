using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

/// <summary>
/// Adapts the Bybit-specific provider factory to the application exchange boundary.
/// </summary>
public sealed class BybitPrivateAccountProviderFactoryAdapter(
    BybitPrivateAccountProviderFactory providerFactory)
    : IPrivateAccountProviderFactory
{
    public IPrivateAccountProviderLease Create(
        ExchangeId exchange,
        ExchangeAccountCredential credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (exchange != ExchangeId.Bybit)
        {
            throw new NotSupportedException($"Exchange '{exchange}' is not supported.");
        }

        var bybitLease = credentials.Use<BybitPrivateAccountProviderLease>(
            (apiKey, apiSecret) =>
                providerFactory.Create(new BybitCredentials(apiKey, apiSecret)));
        return new Lease(bybitLease);
    }

    private sealed class Lease(BybitPrivateAccountProviderLease bybitLease)
        : IPrivateAccountProviderLease
    {
        public IPrivateAccountProvider Provider => bybitLease.Provider;

        public void Dispose() => bybitLease.Dispose();
    }
}
