using System.Collections.Concurrent;
using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Interfaces.Clients.V5;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Exchanges.Bybit.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Intelligence.TradeSystem.Exchanges.Tests.Realtime;

internal sealed class RecordingObserver : IExchangeRealtimeObserver
{
    private readonly ConcurrentQueue<PrivatePositionObservation> _positions = new();
    private readonly ConcurrentQueue<PrivateWalletObservation> _wallets = new();
    private readonly ConcurrentQueue<RealtimeTransportState> _transportStates = new();

    public Action<PrivatePositionObservation>? OnPositionCallback { get; set; }

    public IReadOnlyList<PrivatePositionObservation> Positions => _positions.ToArray();

    public IReadOnlyList<PrivateWalletObservation> Wallets => _wallets.ToArray();

    public IReadOnlyList<RealtimeTransportState> TransportStates => _transportStates.ToArray();

    public void OnPosition(PrivatePositionObservation observation)
    {
        _positions.Enqueue(observation);
        OnPositionCallback?.Invoke(observation);
    }

    public void OnWallet(PrivateWalletObservation observation) => _wallets.Enqueue(observation);

    public void OnTransportState(RealtimeTransportState state) => _transportStates.Enqueue(state);
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed record CapturedLog(
    LogLevel LogLevel,
    EventId EventId,
    IReadOnlyList<KeyValuePair<string, object?>> Fields,
    string Message);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structuredState
                ? structuredState.ToList()
                : [];
            entries.Enqueue(new CapturedLog(logLevel, eventId, fields, formatter(state, exception)));
        }
    }
}

internal static class InstrumentMetadata
{
    public static Mock<IBybitRestClientApiExchangeData> ExchangeData(params (string Symbol, string SettleAsset)[] instruments)
    {
        var exchangeData = new Mock<IBybitRestClientApiExchangeData>();
        exchangeData
            .Setup(data => data.GetLinearInverseSymbolsAsync(
                Category.Linear, null, null, null, null, BybitInstrumentSettlementResolver.PageLimit, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(null, instruments));
        return exchangeData;
    }

    public static HttpResult<BybitResponse<BybitLinearInverseSymbol>> Page(
        string? nextPageCursor,
        params (string Symbol, string SettleAsset)[] instruments) =>
        new(
            "Bybit",
            new BybitResponse<BybitLinearInverseSymbol>
            {
                List = [.. instruments.Select(instrument => new BybitLinearInverseSymbol
                {
                    Name = instrument.Symbol,
                    SettleAsset = instrument.SettleAsset,
                })],
                NextPageCursor = nextPageCursor,
            },
            null!);

    public static BybitInstrumentSettlementResolver Resolver(
        Mock<IBybitRestClientApiExchangeData> exchangeData,
        TimeProvider? timeProvider = null,
        ILogger<BybitInstrumentSettlementResolver>? logger = null)
    {
        var v5Api = new Mock<IBybitRestClientApi>();
        v5Api.SetupGet(api => api.ExchangeData).Returns(exchangeData.Object);
        var client = new Mock<IBybitRestClient>();
        client.SetupGet(restClient => restClient.V5Api).Returns(v5Api.Object);
        return new BybitInstrumentSettlementResolver(
            client.Object,
            logger ?? NullLogger<BybitInstrumentSettlementResolver>.Instance,
            timeProvider ?? TimeProvider.System);
    }

    public static BybitInstrumentSettlementResolver Resolver(params (string Symbol, string SettleAsset)[] instruments) =>
        Resolver(ExchangeData(instruments));
}
