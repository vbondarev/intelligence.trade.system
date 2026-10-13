using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

/// <summary>
/// Владеет websocket-клиентом аккаунта и его провайдером; освобождение закрывает подписки и соединение.
/// </summary>
public sealed class BybitRealtimeProviderLease : IExchangeRealtimeProvider
{
    private readonly IBybitSocketClient _client;
    private readonly BybitRealtimeProvider _provider;
    private int _disposed;

    internal BybitRealtimeProviderLease(IBybitSocketClient client, BybitRealtimeProvider provider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ExchangeAccountId ExchangeAccountId => _provider.ExchangeAccountId;

    public Task<RealtimeTransportState> StartAsync(
        IExchangeRealtimeObserver observer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _provider.StartAsync(observer, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await _provider.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _client.Dispose();
        }
    }
}
