using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects.Sockets;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

public sealed partial class BybitPublicMarkPriceProvider : IMarketMarkPriceSubscriptionProvider, IAsyncDisposable
{
    private readonly IBybitSocketClient _client;
    private readonly ILogger<BybitPublicMarkPriceProvider> _logger;
    private int _disposed;

    public BybitPublicMarkPriceProvider(ILogger<BybitPublicMarkPriceProvider> logger)
        : this(BybitClientFactory.CreatePublicSocketClient(), logger, ownsClient: true)
    {
    }

    internal BybitPublicMarkPriceProvider(
        IBybitSocketClient client,
        ILogger<BybitPublicMarkPriceProvider> logger,
        bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);
        _client = client;
        _logger = logger;
        _ownsClient = ownsClient;
    }

    private readonly bool _ownsClient;

    public async Task<IAsyncDisposable> SubscribeAsync(
        InstrumentId symbol,
        Action<MarketMarkPriceObservation> onObservation,
        Action onFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onObservation);
        ArgumentNullException.ThrowIfNull(onFailure);
        if (symbol == default
            || string.IsNullOrWhiteSpace(symbol.Value)
            || symbol.Value.Length > MarketMarkPrice.SymbolMaxLength)
        {
            throw new ArgumentException("Symbol must be initialized and not too long.", nameof(symbol));
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var result = await _client.V5LinearApi.SubscribeToTickerUpdatesAsync(
            symbol.Value,
            update => HandleUpdate(update, onObservation),
            cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data is null)
        {
            if (result.Data is { } rejectedSubscription)
            {
                await rejectedSubscription.CloseAsync().ConfigureAwait(false);
            }

            throw new InvalidOperationException("Bybit public mark-price subscription failed.");
        }

        result.Data.ConnectionLost += onFailure;
        result.Data.ConnectionClosed += onFailure;
        result.Data.ResubscribingFailed += _ => onFailure();
        return new SubscriptionLease(result.Data);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsClient)
        {
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void HandleUpdate(
        DataEvent<BybitLinearTickerUpdate> update,
        Action<MarketMarkPriceObservation> onObservation)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            if (BybitRealtimeObservationMapper.MapMarkPrice(update) is { } observation)
            {
                onObservation(observation);
            }
        }
        catch (Exception exception)
        {
            LogMessageRejected(_logger, exception.GetType().Name);
        }
    }

    private sealed class SubscriptionLease(UpdateSubscription subscription) : IAsyncDisposable
    {
        private UpdateSubscription? _subscription = subscription;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _subscription, null) is { } activeSubscription)
            {
                await activeSubscription.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Warning,
        Message = "Обработчик публичного mark price Bybit отклонил сообщение. ExceptionType={ExceptionType}")]
    private static partial void LogMessageRejected(ILogger logger, string exceptionType);
}
