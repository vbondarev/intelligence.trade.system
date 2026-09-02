using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

public sealed class BybitPrivateAccountProviderLease : IDisposable
{
    private readonly IBybitRestClient _client;
    private int _disposed;

    public IPrivateAccountProvider Provider { get; }

    internal BybitPrivateAccountProviderLease(IBybitRestClient client, IPrivateAccountProvider provider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        GC.SuppressFinalize(this);
        _client.Dispose();
    }
}
