using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Exchanges.Bybit.ClientFactory;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

/// <summary>
/// Создаёт приватный потоковый провайдер Bybit с собственным websocket-клиентом для каждого аккаунта.
/// </summary>
public sealed class BybitRealtimeProviderFactory : IExchangeRealtimeProviderFactory
{
    private readonly BybitInstrumentSettlementResolver _settlementResolver;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<BybitCredentials, IBybitSocketClient> _clientFactory;

    public BybitRealtimeProviderFactory(
        BybitInstrumentSettlementResolver settlementResolver,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider)
        : this(settlementResolver, loggerFactory, timeProvider, BybitClientFactory.CreatePrivateSocketClient)
    {
    }

    internal BybitRealtimeProviderFactory(
        BybitInstrumentSettlementResolver settlementResolver,
        ILoggerFactory loggerFactory,
        TimeProvider timeProvider,
        Func<BybitCredentials, IBybitSocketClient> clientFactory)
    {
        ArgumentNullException.ThrowIfNull(settlementResolver);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(clientFactory);

        _settlementResolver = settlementResolver;
        _loggerFactory = loggerFactory;
        _timeProvider = timeProvider;
        _clientFactory = clientFactory;
    }

    public IExchangeRealtimeProvider Create(
        ExchangeId exchange,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredential credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (exchange != ExchangeId.Bybit)
        {
            throw new NotSupportedException($"Exchange '{exchange}' is not supported.");
        }

        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        var client = credentials.Use(
            (apiKey, apiSecret) => _clientFactory(new BybitCredentials(apiKey, apiSecret)));
        try
        {
            var provider = new BybitRealtimeProvider(
                client,
                _settlementResolver,
                exchangeAccountId,
                _loggerFactory.CreateLogger<BybitRealtimeProvider>(),
                _timeProvider);
            return new BybitRealtimeProviderLease(client, provider);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
