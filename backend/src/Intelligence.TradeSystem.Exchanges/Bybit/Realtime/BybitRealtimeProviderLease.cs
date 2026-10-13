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

    // Единственная задача освобождения: повторные и конкурентные вызовы ожидают её, а не возвращаются раньше.
    private readonly Lazy<Task> _disposal;

    internal BybitRealtimeProviderLease(IBybitSocketClient client, BybitRealtimeProvider provider)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _disposal = new Lazy<Task>(DisposeCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ExchangeAccountId ExchangeAccountId => _provider.ExchangeAccountId;

    public Task<RealtimeTransportState> StartAsync(
        IExchangeRealtimeObserver observer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposal.IsValueCreated, this);
        return _provider.StartAsync(observer, cancellationToken);
    }

    /// <summary>
    /// Останавливает провайдер, дожидаясь выполняющихся callbacks получателя, и только затем
    /// освобождает websocket-клиент.
    /// </summary>
    public ValueTask DisposeAsync() => new(_disposal.Value);

    private async Task DisposeCoreAsync()
    {
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
