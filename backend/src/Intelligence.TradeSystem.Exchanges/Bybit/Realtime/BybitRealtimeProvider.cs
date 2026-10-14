using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using CryptoExchange.Net.Objects.Sockets;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Realtime;

/// <summary>
/// Приватные потоки позиций и кошелька одного аккаунта Bybit поверх нативного V5 websocket API.
/// </summary>
/// <remarks>
/// <para>
/// Обработчики выполняются в receive loop SDK: исключение в нём останавливает обработку
/// соединения, поэтому обработчики перехватывают все ошибки и сообщают
/// <see cref="RealtimeTransportStatus.MessageRejected"/>. Переподключение выполняет SDK; провайдер
/// только сообщает о потере и восстановлении транспорта.
/// </para>
/// <para>
/// Ревизия позиции считается доставленной только после успешного возврата
/// <see cref="IExchangeRealtimeObserver.OnPosition"/>. Если получатель не принял и сигнал о
/// необходимости REST recovery, сигнал повторяется перед следующим вызовом получателя, а
/// наблюдения до его принятия не доставляются.
/// </para>
/// </remarks>
internal sealed partial class BybitRealtimeProvider
{
    internal const string PositionStream = "position";
    internal const string WalletStream = "wallet";

    // Префикс, которым CryptoExchange.Net помечает отказ аутентификации websocket-соединения.
    private const string AuthenticationFailedPrefix = "Authentication failed";

    private const int Created = 0;
    private const int Starting = 1;
    private const int Running = 2;
    private const int Stopped = 3;

    private readonly IBybitSocketClient _client;
    private readonly BybitInstrumentSettlementResolver _settlementResolver;
    private readonly ILogger<BybitRealtimeProvider> _logger;
    private readonly TimeProvider _timeProvider;

    // Защищает жизненный цикл: состояние, допуск callbacks получателя и подписки. Под ним не
    // вызываются получатель и асинхронные операции SDK.
    private readonly Lock _gate = new();
    private readonly List<UpdateSubscription> _subscriptions = [];

    // Защищает подтверждённые ревизии и счётчик порядка классификации. Под ним не вызывается
    // получатель и не захватывается _gate.
    private readonly Lock _revisionsGate = new();
    private readonly Dictionary<PositionStreamKey, DeliveredRevision> _deliveredRevisions = [];
    private long _classifiedRevisions;
    private IExchangeRealtimeObserver? _observer;
    private int _state;
    private int _activeCallbacks;
    private TaskCompletionSource? _callbacksDrained;
    private TaskCompletionSource? _stopCompletion;

    // Число сигналов REST recovery, не принятых получателем, и сколько из них покрыто позже
    // доставленным сигналом. Пока покрытие отстаёт, recovery ожидает повторной доставки.
    private long _recoveryDemand;
    private long _recoveryCovered;

    public BybitRealtimeProvider(
        IBybitSocketClient client,
        BybitInstrumentSettlementResolver settlementResolver,
        ExchangeAccountId exchangeAccountId,
        ILogger<BybitRealtimeProvider> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settlementResolver);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (exchangeAccountId == default)
        {
            throw new ArgumentException("ExchangeAccountId must be initialized.", nameof(exchangeAccountId));
        }

        _client = client;
        _settlementResolver = settlementResolver;
        ExchangeAccountId = exchangeAccountId;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public ExchangeAccountId ExchangeAccountId { get; }

    private bool IsActive => Volatile.Read(ref _state) is Starting or Running;

    public async Task<RealtimeTransportState> StartAsync(
        IExchangeRealtimeObserver observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            if (_state != Created)
            {
                throw new InvalidOperationException("Realtime provider can be started only once.");
            }

            _state = Starting;
            _observer = observer;
        }

        try
        {
            await _settlementResolver.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

            var failure = await SubscribeAsync(
                PositionStream,
                ct => _client.V5PrivateApi.SubscribeToPositionUpdatesAsync(HandlePositions, ct),
                cancellationToken).ConfigureAwait(false);
            failure ??= await SubscribeAsync(
                WalletStream,
                ct => _client.V5PrivateApi.SubscribeToWalletUpdatesAsync(HandleWallet, ct),
                cancellationToken).ConfigureAwait(false);

            if (failure is not null)
            {
                await StopAsync().ConfigureAwait(false);
                return new RealtimeTransportState(RealtimeTransportStatus.SubscribeFailed, _timeProvider.GetUtcNow(), failure);
            }

            bool running;
            lock (_gate)
            {
                running = _state == Starting;
                if (running)
                {
                    _state = Running;
                }
            }

            ObjectDisposedException.ThrowIf(!running, this);

            return new RealtimeTransportState(RealtimeTransportStatus.Subscribed, _timeProvider.GetUtcNow());
        }
        catch
        {
            await StopAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Запрещает новые callbacks получателя, дожидается уже выполняющихся и закрывает подписки.
    /// После завершения задачи получатель не вызывается.
    /// </summary>
    /// <remarks>
    /// Повторные и конкурентные вызовы завершаются вместе с первой остановкой. Callback получателя
    /// не должен синхронно ожидать остановку: она ожидает завершения этого callback.
    /// </remarks>
    public async Task StopAsync()
    {
        TaskCompletionSource stopCompletion;
        var callbacksDrained = Task.CompletedTask;
        bool ownsStop;
        lock (_gate)
        {
            ownsStop = _stopCompletion is null;
            stopCompletion = _stopCompletion ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (ownsStop)
            {
                _state = Stopped;
                if (_activeCallbacks > 0)
                {
                    _callbacksDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    callbacksDrained = _callbacksDrained.Task;
                }
            }
        }

        if (!ownsStop)
        {
            await stopCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            await callbacksDrained.ConfigureAwait(false);

            UpdateSubscription[] subscriptions;
            lock (_gate)
            {
                subscriptions = [.. _subscriptions];
                _subscriptions.Clear();
            }

            foreach (var subscription in subscriptions)
            {
                await CloseQuietlyAsync(subscription).ConfigureAwait(false);
            }
        }
        finally
        {
            stopCompletion.TrySetResult();
        }
    }

    internal void HandlePositions(DataEvent<BybitPositionUpdate[]> update)
    {
        if (!IsActive)
        {
            return;
        }

        IReadOnlyList<PrivatePositionObservation> observations;
        try
        {
            observations = BybitRealtimeObservationMapper.MapPositions(
                update,
                ExchangeAccountId,
                ResolveSettlement);
        }
        catch (Exception exception)
        {
            RejectMessage(PositionStream, exception);
            return;
        }

        foreach (var observation in observations)
        {
            // Оставшиеся наблюдения покрывает либо остановка, либо ещё не принятый сигнал recovery.
            if (DeliverPosition(observation) is DeliveryOutcome.Skipped or DeliveryOutcome.Deferred)
            {
                return;
            }
        }
    }

    internal void HandleWallet(DataEvent<BybitBalance[]> update)
    {
        if (!IsActive)
        {
            return;
        }

        PrivateWalletObservation observation;
        try
        {
            observation = BybitRealtimeObservationMapper.MapWallet(update, ExchangeAccountId);
        }
        catch (Exception exception)
        {
            RejectMessage(WalletStream, exception);
            return;
        }

        DeliverObservation(WalletStream, observer => observer.OnWallet(observation));
    }

    private async Task<ExchangeFailure?> SubscribeAsync(
        string stream,
        Func<CancellationToken, Task<WebSocketResult<UpdateSubscription>>> subscribe,
        CancellationToken cancellationToken)
    {
        var result = await subscribe(cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            BybitExchangeFailureMapper.ThrowIfCancellationRequested(result.Error, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var failure = MapFailure(result.Error);
            LogSubscriptionFailed(
                _logger,
                stream,
                failure.Kind,
                failure.Retryable,
                BybitExchangeFailureMapper.SafeProviderCode(failure.ProviderCode));
            return failure;
        }

        await TrackAsync(stream, result.Data).ConfigureAwait(false);
        return null;
    }

    private async Task TrackAsync(string stream, UpdateSubscription subscription)
    {
        lock (_gate)
        {
            if (_state is Starting or Running)
            {
                _subscriptions.Add(subscription);
                subscription.ConnectionLost += () => ReportTransport(stream, RealtimeTransportStatus.ConnectionLost);
                subscription.ConnectionClosed += () => ReportTransport(stream, RealtimeTransportStatus.ConnectionLost);
                subscription.ConnectionRestored += _ => ReportTransport(stream, RealtimeTransportStatus.Restored);
                subscription.ResubscribingFailed += error =>
                    ReportTransport(stream, RealtimeTransportStatus.ResubscribeFailed, MapFailure(error));
                return;
            }
        }

        // Провайдер освобождён во время подписки: подписка не должна пережить провайдер.
        await CloseQuietlyAsync(subscription).ConfigureAwait(false);
    }

    private void ReportTransport(string stream, RealtimeTransportStatus status, ExchangeFailure? failure = null)
    {
        if (!IsActive)
        {
            return;
        }

        LogTransportStateChanged(
            _logger,
            stream,
            status,
            failure?.Kind,
            BybitExchangeFailureMapper.SafeProviderCode(failure?.ProviderCode));
        ReportRecoveryRequired(stream, status, failure);
    }

    private void RejectMessage(string stream, Exception exception)
    {
        LogMessageRejected(_logger, stream, exception.GetType().Name);
        ReportRecoveryRequired(stream, RealtimeTransportStatus.MessageRejected);
    }

    /// <summary>
    /// Доставляет наблюдение позиции; ревизия подтверждается только после успешного возврата
    /// получателя, поэтому повтор непринятого наблюдения не становится
    /// <see cref="PrivatePositionObservationKind.Duplicate"/>.
    /// </summary>
    private DeliveryOutcome DeliverPosition(PrivatePositionObservation observation)
    {
        var classified = ClassifyRepeat(observation, out var candidate);
        var outcome = DeliverObservation(PositionStream, observer => observer.OnPosition(classified));
        if (outcome == DeliveryOutcome.Delivered)
        {
            RecordDeliveredRevision(candidate);
        }

        return outcome;
    }

    /// <summary>
    /// Вызывает получателя наблюдения, предварительно повторив не принятый им сигнал recovery:
    /// наблюдение не должно опередить сведения о возможной потере предыдущих. Непринятое
    /// получателем наблюдение потеряно, поэтому о нём сообщается
    /// <see cref="RealtimeTransportStatus.MessageRejected"/>.
    /// </summary>
    private DeliveryOutcome DeliverObservation(string stream, Action<IExchangeRealtimeObserver> notify)
    {
        if (Interlocked.Read(ref _recoveryDemand) > Interlocked.Read(ref _recoveryCovered))
        {
            var recovery = ReportRecoveryRequired(stream, RealtimeTransportStatus.MessageRejected);
            if (recovery != DeliveryOutcome.Delivered)
            {
                return recovery == DeliveryOutcome.Skipped ? DeliveryOutcome.Skipped : DeliveryOutcome.Deferred;
            }
        }

        var outcome = Notify(stream, notify);
        if (outcome == DeliveryOutcome.Rejected)
        {
            ReportRecoveryRequired(stream, RealtimeTransportStatus.MessageRejected);
        }

        return outcome;
    }

    /// <summary>
    /// Сообщает состояние, требующее REST recovery. Отказ получателя не порождает новый
    /// <see cref="RealtimeTransportStatus.MessageRejected"/>, а оставляет recovery ожидающим повторной
    /// доставки; доставленный сигнал покрывает все отказы, зафиксированные до его отправки.
    /// </summary>
    private DeliveryOutcome ReportRecoveryRequired(
        string stream,
        RealtimeTransportStatus status,
        ExchangeFailure? failure = null)
    {
        var demand = Interlocked.Read(ref _recoveryDemand);
        var state = new RealtimeTransportState(status, _timeProvider.GetUtcNow(), failure);
        var outcome = Notify(stream, observer => observer.OnTransportState(state));
        if (outcome == DeliveryOutcome.Delivered)
        {
            CoverRecoveryDemand(demand);
        }
        else if (outcome == DeliveryOutcome.Rejected)
        {
            Interlocked.Increment(ref _recoveryDemand);
            LogRecoveryNotAccepted(_logger, stream, status);
        }

        return outcome;
    }

    private void CoverRecoveryDemand(long demand)
    {
        var covered = Interlocked.Read(ref _recoveryCovered);
        while (covered < demand)
        {
            var observed = Interlocked.CompareExchange(ref _recoveryCovered, demand, covered);
            if (observed == covered)
            {
                return;
            }

            covered = observed;
        }
    }

    /// <summary>
    /// Вызывает получателя, только если остановка ещё не началась; выполняющийся вызов учитывается,
    /// чтобы <see cref="StopAsync"/> дождался его завершения.
    /// </summary>
    private DeliveryOutcome Notify(string stream, Action<IExchangeRealtimeObserver> notify)
    {
        IExchangeRealtimeObserver observer;
        lock (_gate)
        {
            if (_state is not (Starting or Running) || _observer is null)
            {
                return DeliveryOutcome.Skipped;
            }

            observer = _observer;
            _activeCallbacks++;
        }

        try
        {
            notify(observer);
            return DeliveryOutcome.Delivered;
        }
        catch (Exception exception)
        {
            LogObserverFailed(_logger, stream, exception.GetType().Name);
            return DeliveryOutcome.Rejected;
        }
        finally
        {
            ExitCallback();
        }
    }

    private void ExitCallback()
    {
        TaskCompletionSource? callbacksDrained = null;
        lock (_gate)
        {
            if (--_activeCallbacks == 0)
            {
                callbacksDrained = _callbacksDrained;
                _callbacksDrained = null;
            }
        }

        callbacksDrained?.TrySetResult();
    }

    private SettlementAsset? ResolveSettlement(string symbol) =>
        _settlementResolver.TryResolve(symbol, out var settlementAsset) ? settlementAsset : null;

    /// <summary>
    /// Помечает точный повтор последней доставленной ревизии позиции, не подтверждая текущую.
    /// Порядок относительно сохранённых курсоров здесь не определяется.
    /// </summary>
    /// <param name="candidate">
    /// Ревизия для подтверждения после успешной доставки; <see langword="null"/>, если наблюдение
    /// не участвует в определении повторов.
    /// </param>
    private PrivatePositionObservation ClassifyRepeat(
        PrivatePositionObservation observation,
        out RevisionCandidate? candidate)
    {
        candidate = null;
        if (observation.Kind is not (PrivatePositionObservationKind.Open or PrivatePositionObservationKind.PotentialClose)
            || observation.Category is not { } category)
        {
            return observation;
        }

        var key = new PositionStreamKey(category, observation.Symbol, observation.PositionIdx);
        var revision = new PositionRevision(
            observation.Kind,
            observation.Side,
            observation.Size,
            observation.ProviderSequence,
            observation.ProviderUpdatedAt,
            observation.SettlementAsset,
            observation.Position);
        bool isRepeat;
        lock (_revisionsGate)
        {
            candidate = new RevisionCandidate(key, revision, ++_classifiedRevisions);
            isRepeat = _deliveredRevisions.TryGetValue(key, out var delivered) && delivered.Revision == revision;
        }

        return isRepeat ? observation with { Kind = PrivatePositionObservationKind.Duplicate } : observation;
    }

    /// <summary>
    /// Подтверждает доставленную ревизию. Callbacks могут завершаться не в порядке классификации,
    /// поэтому ревизия, классифицированная раньше уже подтверждённой, её не заменяет.
    /// </summary>
    private void RecordDeliveredRevision(RevisionCandidate? candidate)
    {
        if (candidate is not { } revision)
        {
            return;
        }

        lock (_revisionsGate)
        {
            if (!_deliveredRevisions.TryGetValue(revision.Key, out var delivered) || delivered.Order < revision.Order)
            {
                _deliveredRevisions[revision.Key] = new DeliveredRevision(revision.Revision, revision.Order);
            }
        }
    }

    private static ExchangeFailure MapFailure(Error? error)
    {
        var failure = BybitExchangeFailureMapper.Map(error);
        return failure.Kind == ExchangeFailureKind.Unknown
            && error?.Message?.StartsWith(AuthenticationFailedPrefix, StringComparison.Ordinal) == true
                ? new ExchangeFailure(ExchangeFailureKind.InvalidCredentials, Retryable: false, failure.ProviderCode)
                : failure;
    }

    private async Task CloseQuietlyAsync(UpdateSubscription subscription)
    {
        try
        {
            await subscription.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogUnsubscribeFailed(_logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(
        EventId = 1013,
        Level = LogLevel.Warning,
        Message = "Подписка на поток Bybit отклонена. Stream={Stream}, FailureKind={FailureKind}, Retryable={Retryable}, ProviderCode={ProviderCode}")]
    private static partial void LogSubscriptionFailed(
        ILogger logger,
        string stream,
        ExchangeFailureKind failureKind,
        bool retryable,
        string? providerCode);

    [LoggerMessage(
        EventId = 1014,
        Level = LogLevel.Warning,
        Message = "Состояние транспорта потока Bybit изменилось. Stream={Stream}, Status={Status}, FailureKind={FailureKind}, ProviderCode={ProviderCode}")]
    private static partial void LogTransportStateChanged(
        ILogger logger,
        string stream,
        RealtimeTransportStatus status,
        ExchangeFailureKind? failureKind,
        string? providerCode);

    [LoggerMessage(
        EventId = 1015,
        Level = LogLevel.Warning,
        Message = "Сообщение потока Bybit не нормализовано и отброшено. Stream={Stream}, ExceptionType={ExceptionType}")]
    private static partial void LogMessageRejected(ILogger logger, string stream, string exceptionType);

    [LoggerMessage(
        EventId = 1016,
        Level = LogLevel.Error,
        Message = "Получатель наблюдений потока Bybit завершился ошибкой. Stream={Stream}, ExceptionType={ExceptionType}")]
    private static partial void LogObserverFailed(ILogger logger, string stream, string exceptionType);

    [LoggerMessage(
        EventId = 1017,
        Level = LogLevel.Warning,
        Message = "Не удалось закрыть подписку потока Bybit. ExceptionType={ExceptionType}")]
    private static partial void LogUnsubscribeFailed(ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 1018,
        Level = LogLevel.Error,
        Message = "Получатель не принял сигнал о необходимости REST recovery; сигнал будет повторён перед следующим наблюдением. Stream={Stream}, Status={Status}")]
    private static partial void LogRecoveryNotAccepted(ILogger logger, string stream, RealtimeTransportStatus status);

    private enum DeliveryOutcome
    {
        /// <summary>Получатель вернул управление без исключения.</summary>
        Delivered,

        /// <summary>Получатель завершился исключением и не принял вызов.</summary>
        Rejected,

        /// <summary>Получатель не вызван: он ещё не принял сигнал о необходимости REST recovery.</summary>
        Deferred,

        /// <summary>Получатель не вызван: остановка провайдера уже началась.</summary>
        Skipped,
    }

    private readonly record struct PositionStreamKey(MarketCategory Category, string Symbol, int PositionIdx);

    /// <param name="Order">Порядок классификации наблюдения внутри провайдера.</param>
    private readonly record struct RevisionCandidate(PositionStreamKey Key, PositionRevision Revision, long Order);

    private readonly record struct DeliveredRevision(PositionRevision Revision, long Order);

    private sealed record PositionRevision(
        PrivatePositionObservationKind Kind,
        PositionSide Side,
        decimal Size,
        long? Sequence,
        DateTimeOffset? UpdatedAt,
        SettlementAsset? SettlementAsset,
        OpenPosition? Position);
}
