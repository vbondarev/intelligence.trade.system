using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

internal enum SettlementMetadataRefreshResult
{
    Skipped,
    Succeeded,
    Failed,
}

/// <summary>
/// Периодически проверяет TTL метаданных и объединяет запросы от неизвестных инструментов.
/// </summary>
/// <remarks>
/// Цикл принадлежит одному realtime provider, а общий resolver сериализует запросы всех providers.
/// Публикуемый resolver снимок заменяется только после полного подтверждённого обхода страниц.
/// </remarks>
internal sealed partial class BybitSettlementMetadataRefreshLoop : IAsyncDisposable
{
    internal static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(1);

    private readonly BybitInstrumentSettlementResolver _resolver;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _stop = new();
    private readonly ITimer _timer;
    private Task? _runTask;
    private int _started;
    private int _missingSymbolRequested;
    private int _disposed;

    public BybitSettlementMetadataRefreshLoop(
        BybitInstrumentSettlementResolver resolver,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _resolver = resolver;
        _timeProvider = timeProvider;
        _logger = logger;
        _timer = _timeProvider.CreateTimer(
            static state => ((BybitSettlementMetadataRefreshLoop)state!).Signal(),
            this,
            MaintenanceInterval,
            MaintenanceInterval);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Цикл обновления метаданных можно запустить только один раз.");
        }

        _runTask = RunAsync(_stop.Token);
    }

    public void RequestMissingSymbolRefresh()
    {
        Interlocked.Exchange(ref _missingSymbolRequested, 1);
        Signal();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            if (_runTask is { } existingRunTask)
            {
                await existingRunTask.ConfigureAwait(false);
            }

            return;
        }

        _stop.Cancel();
        _timer.Dispose();
        _signals.Writer.TryComplete();

        if (_runTask is { } runTask)
        {
            await runTask.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private void Signal() => _signals.Writer.TryWrite(0);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_signals.Reader.TryRead(out _))
                {
                }

                var missingSymbol = Interlocked.Exchange(ref _missingSymbolRequested, 0) != 0;
                try
                {
                    var result = await _resolver
                        .RefreshIfDueAsync(missingSymbol, cancellationToken)
                        .ConfigureAwait(false);
                    if (missingSymbol && result != SettlementMetadataRefreshResult.Succeeded)
                    {
                        Interlocked.Exchange(ref _missingSymbolRequested, 1);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    LogRefreshFailedUnexpectedly(_logger, exception.GetType().Name);
                    if (missingSymbol)
                    {
                        Interlocked.Exchange(ref _missingSymbolRequested, 1);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(
        EventId = 1014,
        Level = LogLevel.Warning,
        Message = "Цикл обновления метаданных инструментов Bybit завершил попытку с ошибкой. ExceptionType={ExceptionType}")]
    private static partial void LogRefreshFailedUnexpectedly(ILogger logger, string exceptionType);
}
