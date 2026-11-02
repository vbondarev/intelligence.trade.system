using System.Runtime.ExceptionServices;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
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
        cancellationToken.ThrowIfCancellationRequested();

        var result = await SubscribeToTickerUpdatesAsync(
            symbol,
            onObservation,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data is null)
        {
            if (result.Data is { } rejectedSubscription)
            {
                var rejectedLease = new BybitPublicMarkPriceSubscriptionLease(
                    rejectedSubscription.CloseAsync);
                try
                {
                    await rejectedLease.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    throw new MarketPriceSubscriptionStartUnconfirmedException(
                        exception,
                        rejectedLease);
                }
            }

            if (result.Data is null)
            {
                throw new MarketPriceSubscriptionStartUnconfirmedException();
            }

            throw new InvalidOperationException("Bybit public mark-price subscription failed.");
        }

        var subscription = result.Data;
        var lease = new BybitPublicMarkPriceSubscriptionLease(subscription.CloseAsync);
        try
        {
            subscription.ConnectionLost += onFailure;
            subscription.ConnectionClosed += onFailure;
            subscription.ResubscribingFailed += _ => onFailure();
            return lease;
        }
        catch (Exception exception)
        {
            try
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new MarketPriceSubscriptionStartUnconfirmedException(
                    cleanupException,
                    lease);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
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

    private async Task<WebSocketResult<UpdateSubscription>>
        SubscribeToTickerUpdatesAsync(
            InstrumentId symbol,
            Action<MarketMarkPriceObservation> onObservation,
            CancellationToken cancellationToken)
    {
            var api = _client.V5LinearApi;
            try
            {
                return await api.SubscribeToTickerUpdatesAsync(
                    symbol.Value,
                    update => HandleUpdate(update, onObservation),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new MarketPriceSubscriptionStartUnconfirmedException(exception);
        }
    }

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Warning,
        Message = "Обработчик публичного mark price Bybit отклонил сообщение. ExceptionType={ExceptionType}")]
    private static partial void LogMessageRejected(ILogger logger, string exceptionType);
}

internal sealed class BybitPublicMarkPriceSubscriptionLease(Func<Task> closeAsync) : IAsyncDisposable
{
    private readonly Func<Task> _closeAsync = closeAsync
        ?? throw new ArgumentNullException(nameof(closeAsync));
    private readonly SemaphoreSlim _disposeSemaphore = new(1, 1);
    private ExceptionDispatchInfo? _closeFailure;
    private CloseState _state;

    public async ValueTask DisposeAsync()
    {
        await _disposeSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state == CloseState.Closed)
            {
                return;
            }

            if (_state == CloseState.Unconfirmed)
            {
                _closeFailure!.Throw();
                return;
            }

            _state = CloseState.Closing;
            try
            {
                await _closeAsync().ConfigureAwait(false);
                _state = CloseState.Closed;
            }
            catch (Exception exception)
            {
                _closeFailure = ExceptionDispatchInfo.Capture(exception);
                _state = CloseState.Unconfirmed;
                throw;
            }
        }
        finally
        {
            _disposeSemaphore.Release();
        }
    }

    private enum CloseState
    {
        NotRequested,
        Closing,
        Closed,
        Unconfirmed,
    }
}
