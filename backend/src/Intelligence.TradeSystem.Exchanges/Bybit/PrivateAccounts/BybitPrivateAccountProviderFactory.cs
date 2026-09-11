using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

/// <summary>
/// Создаёт аренду приватного провайдера для конкретной учётной записи, не сохраняя учётные данные в DI.
/// </summary>
public sealed class BybitPrivateAccountProviderFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<BybitCredentials, IBybitRestClient> _clientFactory;

    public BybitPrivateAccountProviderFactory(ILoggerFactory loggerFactory)
        : this(loggerFactory, BybitClientFactory.CreatePrivateClient)
    {
    }

    public BybitPrivateAccountProviderFactory(
        ILoggerFactory loggerFactory,
        Func<BybitCredentials, IBybitRestClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(clientFactory);

        _loggerFactory = loggerFactory;
        _clientFactory = clientFactory;
    }

    public BybitPrivateAccountProviderLease Create(BybitCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var client = _clientFactory(credentials);
        var provider = new BybitPrivateAccountProvider(
            client,
            _loggerFactory.CreateLogger<BybitPrivateAccountProvider>());

        return new BybitPrivateAccountProviderLease(client, provider);
    }
}
