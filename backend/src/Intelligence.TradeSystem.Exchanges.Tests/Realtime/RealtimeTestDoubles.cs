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

internal enum ObserverCallKind
{
    Position,
    Wallet,
    TransportState,
}

/// <param name="StartedAt">Порядковый номер входа в вызов среди всех событий получателя.</param>
/// <param name="CompletedAt">Порядковый номер завершения вызова среди всех событий получателя.</param>
/// <param name="Accepted">Вызов вернул управление без исключения.</param>
internal sealed record ObserverCall(ObserverCallKind Kind, long StartedAt, long CompletedAt, bool Accepted);

internal sealed class RecordingObserver : IExchangeRealtimeObserver
{
    private readonly ConcurrentQueue<PrivatePositionObservation> _positions = new();
    private readonly ConcurrentQueue<PrivateWalletObservation> _wallets = new();
    private readonly ConcurrentQueue<RealtimeTransportState> _transportStates = new();
    private readonly ConcurrentQueue<ObserverCall> _calls = new();
    private long _clock;
    private int _inFlight;
    private int _maxInFlight;

    public Action<PrivatePositionObservation>? OnPositionCallback { get; set; }

    public Action<PrivateWalletObservation>? OnWalletCallback { get; set; }

    public Action<RealtimeTransportState>? OnTransportStateCallback { get; set; }

    /// <summary>Все вызовы <see cref="OnPosition"/>, включая завершившиеся исключением.</summary>
    public IReadOnlyList<PrivatePositionObservation> Positions => _positions.ToArray();

    /// <summary>Все вызовы <see cref="OnWallet"/>, включая завершившиеся исключением.</summary>
    public IReadOnlyList<PrivateWalletObservation> Wallets => _wallets.ToArray();

    /// <summary>Все вызовы <see cref="OnTransportState"/>, включая завершившиеся исключением.</summary>
    public IReadOnlyList<RealtimeTransportState> TransportStates => _transportStates.ToArray();

    /// <summary>Завершённые вызовы получателя в порядке входа в них.</summary>
    public IReadOnlyList<ObserverCall> Calls => [.. _calls.OrderBy(call => call.StartedAt)];

    /// <summary>Наибольшее число одновременно выполнявшихся вызовов получателя.</summary>
    public int MaxConcurrentCalls => Volatile.Read(ref _maxInFlight);

    public void OnPosition(PrivatePositionObservation observation)
    {
        _positions.Enqueue(observation);
        Record(ObserverCallKind.Position, () => OnPositionCallback?.Invoke(observation));
    }

    public void OnWallet(PrivateWalletObservation observation)
    {
        _wallets.Enqueue(observation);
        Record(ObserverCallKind.Wallet, () => OnWalletCallback?.Invoke(observation));
    }

    public void OnTransportState(RealtimeTransportState state)
    {
        _transportStates.Enqueue(state);
        Record(ObserverCallKind.TransportState, () => OnTransportStateCallback?.Invoke(state));
    }

    private void Record(ObserverCallKind kind, Action callback)
    {
        var startedAt = Interlocked.Increment(ref _clock);
        var inFlight = Interlocked.Increment(ref _inFlight);
        var max = Volatile.Read(ref _maxInFlight);
        while (inFlight > max)
        {
            var observed = Interlocked.CompareExchange(ref _maxInFlight, inFlight, max);
            if (observed == max)
            {
                break;
            }

            max = observed;
        }

        var accepted = false;
        try
        {
            callback();
            accepted = true;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _calls.Enqueue(new ObserverCall(kind, startedAt, Interlocked.Increment(ref _clock), accepted));
        }
    }
}

/// <summary>
/// Удерживает callback получателя в потоке транспорта до явного освобождения тестом.
/// </summary>
internal sealed class BlockingCallback
{
    private static readonly TimeSpan BlockTimeout = TimeSpan.FromSeconds(10);

    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public void Block()
    {
        _entered.TrySetResult();
        if (!_released.Task.Wait(BlockTimeout))
        {
            throw new TimeoutException("Blocked observer callback was not released.");
        }
    }

    public void Release() => _released.TrySetResult();
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = now;
    private long _timestamp = now.UtcDateTime.Ticks;

    public DateTimeOffset Now
    {
        get
        {
            lock (_sync)
            {
                return _now;
            }
        }
        set
        {
            lock (_sync)
            {
                var elapsed = value - _now;
                _now = value;
                if (elapsed > TimeSpan.Zero)
                {
                    _timestamp += elapsed.Ticks;
                }
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        ManualTimer[] timers;
        lock (_sync)
        {
            _now += elapsed;
            if (elapsed > TimeSpan.Zero)
            {
                _timestamp += elapsed.Ticks;
            }

            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.FireIfDue(Now);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly object _sync = new();
        private DateTimeOffset? _dueAt;
        private TimeSpan _period;
        private bool _disposed;

        public ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _dueAt = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : _owner.GetUtcNow() + dueTime;
                _period = period;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _dueAt = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            lock (_sync)
            {
                if (_disposed || _dueAt is not { } dueAt || dueAt > now)
                {
                    return;
                }

                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? null
                    : now + _period;
            }

            _callback(_state);
        }
    }
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
