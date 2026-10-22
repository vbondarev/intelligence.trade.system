using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

/// <summary>
/// In-memory состояние одного аккаунта с транзакционным откатом: исключение внутри
/// <see cref="IExchangeAccountSyncTransaction.ExecuteAsync"/> восстанавливает все хранилища, как
/// откат транзакции PostgreSQL.
/// </summary>
internal sealed class RealtimeTestWorld
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    public static readonly SettlementAsset Usdt = SettlementAsset.From("USDT");

    private Versioned<ExchangeAccount>? account;
    private Dictionary<PositionId, Versioned<Position>> positions = [];
    private List<PortfolioState> portfolios = [];
    private Dictionary<PositionObservationCursorKey, Versioned<PositionObservationCursor>> cursors = [];
    private List<IApplicationEvent> events = [];
    private Versioned<RealtimeObservationState>? state;
    private List<ExchangeRestOrderingInterval> restOrderingIntervals = [];
    private DateTimeOffset? lastAcceptedWalletMessageCreatedAt;

    /// <param name="observed">
    /// Аккаунт уже наблюдается потоком. Иначе состояние наблюдения отсутствует до
    /// <see cref="StartObservation"/>.
    /// </param>
    /// <param name="recoveryRequired">
    /// Для уже наблюдаемого аккаунта задаёт, ожидает ли сохранённое поколение начальной сверки.
    /// </param>
    public RealtimeTestWorld(bool observed = true, bool recoveryRequired = false)
    {
        account = new Versioned<ExchangeAccount>(
            ExchangeAccount.Create(
                AccountId,
                UserId,
                ExchangeId.Bybit,
                ExchangeAccountProviderIdentity.From("provider-account"),
                "Основной",
                ExchangeAccountConnectionStatus.Connected,
                ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions),
            ConcurrencyVersion.Initial);
        if (observed)
        {
            StartObservation(recoveryRequired);
        }

        Provider = new FakePrivateProvider(this);
    }

    public UserId UserId { get; } = UserId.New();

    public ExchangeAccountId AccountId { get; } = ExchangeAccountId.New();

    public MutableClock Clock { get; } = new(T0.AddMinutes(1));

    public FakePrivateProvider Provider { get; }

    /// <summary>Количество оставшихся искусственных CAS-конфликтов при сохранении курсора.</summary>
    public int CursorConflictsToInject { get; set; }

    /// <summary>Количество искусственных ошибок сохранения состояния наблюдения.</summary>
    public int StateSaveFailuresToInject { get; set; }

    /// <summary>Количество сохранений аккаунта, завершающихся проигранным CAS.</summary>
    public int AccountSaveConflictsToInject { get; set; }

    /// <summary>Количество записей состояния наблюдения, завершающихся проигранным CAS.</summary>
    public int StateConflictsToInject { get; set; }

    /// <summary>Вызывается перед очередной транзакцией применения наблюдения.</summary>
    public Func<CancellationToken, Task>? BeforeTransactionExecuteAsync { get; set; }

    /// <summary>Вызывается перед записью состояния наблюдения с сохраняемым значением.</summary>
    public Action<RealtimeObservationState>? BeforeStateSave { get; set; }

    /// <summary>Вызывается перед чтением состояния наблюдения.</summary>
    public Action? BeforeStateRead { get; set; }

    /// <summary>Количество транзакций, фиксация которых завершится ошибкой после выполнения операции.</summary>
    public int CommitFailuresToInject { get; set; }

    /// <summary>Credentials недоступны при scoped REST-подтверждении позиции.</summary>
    public bool CredentialsUnavailable { get; set; }

    /// <summary>Credentials аккаунта отсутствуют в хранилище.</summary>
    public bool CredentialsMissing { get; set; }

    public int TransactionAttempts { get; private set; }

    public IReadOnlyList<IApplicationEvent> Events => events;

    public IReadOnlyList<PortfolioState> Portfolios => portfolios;

    public IReadOnlyList<ExchangeRestOrderingInterval> RestOrderingIntervals => restOrderingIntervals;

    public IReadOnlyCollection<PositionSlotKey> LastRequestedClosedSlots { get; private set; } = [];

    public DateTimeOffset? LastAcceptedWalletMessageCreatedAt => lastAcceptedWalletMessageCreatedAt;

    /// <summary>Добавляет persisted REST evidence без process-local окон coordinator.</summary>
    public void SeedRestOrderingInterval(ExchangeRestOrderingInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        restOrderingIntervals.Add(interval with { ExchangeAccountId = AccountId });
    }

    public void ReplaceRestOrderingIntervals(params ExchangeRestOrderingInterval[] intervals)
    {
        restOrderingIntervals = intervals
            .Select(interval => interval with { ExchangeAccountId = AccountId })
            .ToList();
    }

    /// <summary>Сводка, которую вернёт <see cref="IPortfolioStateRepository.GetLatestAsync"/>.</summary>
    public PortfolioState? LatestPortfolio =>
        new PortfolioRepository(this).GetLatestAsync(UserId, AccountId).GetAwaiter().GetResult();

    public RealtimeObservationState? State => state?.Value;

    public ExchangeAccount Account => account!.Value;

    public IReadOnlyList<Position> Positions => positions.Values.Select(versioned => versioned.Value).ToArray();

    public IReadOnlyList<Position> ActivePositions =>
        Positions.Where(position => position.TrackingState != PositionTrackingState.Closed).ToArray();

    public PositionObservationCursor? Cursor(string symbol, int positionIdx = 0) =>
        cursors.TryGetValue(
            PositionObservationCursorKey.Create(AccountId, MarketCategory.Linear, InstrumentId.From(symbol), positionIdx),
            out var cursor)
            ? cursor.Value
            : null;

    /// <summary>Сохраняет первое поколение наблюдателя, как <c>AdvanceGenerationAsync</c> без прежнего состояния.</summary>
    public void StartObservation(bool recoveryRequired = true) =>
        state = new Versioned<RealtimeObservationState>(
            new RealtimeObservationState
            {
                ExchangeAccountId = AccountId,
                Generation = 1,
                TransportStatus = RealtimeObservationTransportStatus.Connected,
                RecoveryRequired = recoveryRequired,
                LastFullyReconciledAt = recoveryRequired ? null : T0,
                RecoveryRequestEpoch = 1,
            },
            ConcurrencyVersion.Initial);

    public void AdvanceGeneration()
    {
        var current = state!;
        if (current.Value.RecoveryRequestEpoch == long.MaxValue)
        {
            throw new InvalidOperationException("RecoveryRequestEpoch overflow.");
        }

        state = new Versioned<RealtimeObservationState>(
            current.Value with
            {
                Generation = current.Value.Generation + 1,
                RecoveryRequired = true,
                RecoveryRequestEpoch = current.Value.RecoveryRequestEpoch + 1,
            },
            current.Version.Next());
    }

    /// <summary>
    /// Имитирует concurrent recovery-запрос другого экземпляра: увеличивает epoch и версию.
    /// </summary>
    public void InjectConcurrentRecoveryRequest()
    {
        var current = state ?? throw new InvalidOperationException("Observation state is missing.");
        if (current.Value.RecoveryRequestEpoch == long.MaxValue)
        {
            throw new InvalidOperationException("RecoveryRequestEpoch overflow.");
        }

        state = new Versioned<RealtimeObservationState>(
            current.Value with
            {
                RecoveryRequired = true,
                RecoveryRequestEpoch = current.Value.RecoveryRequestEpoch + 1,
            },
            current.Version.Next());
    }

    /// <summary>Сохраняет готовую позицию аккаунта без курсора, сводки и событий.</summary>
    public Position SeedPosition(
        string symbol,
        DateTimeOffset firstDetectedAt,
        DateTimeOffset lastObservedAt,
        Action<Position>? evolve = null)
    {
        var position = Position.Create(
            ExchangePositionKey.Create(AccountId, InstrumentId.From(symbol), PositionSide.Long, 0),
            MarketCategory.Linear,
            Usdt,
            1m,
            firstDetectedAt,
            lastObservedAt,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            breakEvenPrice: 100m,
            unrealizedPnl: 0m);
        evolve?.Invoke(position);
        SeedPosition(position);
        return position;
    }

    public void DisableAccount()
    {
        var current = account!;
        current.Value.Disable();
        account = new Versioned<ExchangeAccount>(Clone(current.Value), current.Version.Next());
    }

    public void SeedPosition(Position position) =>
        positions[position.Id] = new Versioned<Position>(Clone(position), ConcurrencyVersion.Initial);

    public void SetPositionsWatermark(DateTimeOffset observedAt) =>
        AdvanceWatermark(ExchangeAccountObservationResource.Positions, observedAt);

    /// <summary>Продвигает balance watermark без изменения капитала, как неуспешный REST-запрос баланса.</summary>
    public void SetBalanceWatermark(DateTimeOffset observedAt) =>
        AdvanceWatermark(ExchangeAccountObservationResource.Balance, observedAt);

    private void AdvanceWatermark(ExchangeAccountObservationResource resource, DateTimeOffset observedAt)
    {
        var current = account!;
        var updated = Clone(current.Value);
        updated.AdvanceObservationWatermark(resource, observedAt);
        account = new Versioned<ExchangeAccount>(updated, current.Version.Next());
    }

    public RealtimePositionReconciliationService CreatePositionService() =>
        new(
            new AccountRepository(this),
            CreateCredentialStore(),
            new ProviderFactory(Provider),
            new PositionRepository(this),
            new PortfolioRepository(this),
            new CursorStore(this),
            new StateStore(this),
            new RestOrderingEvidenceStore(this),
            new Transaction(this),
            new Outbox(this),
            Clock);

    public ExchangeAccountSyncService CreateSyncService() =>
        new(
            new AccountRepository(this),
            CreateCredentialStore(),
            new ProviderFactory(Provider),
            new PositionRepository(this),
            new PortfolioRepository(this),
            new Transaction(this),
            new Outbox(this),
            new RestOrderingEvidenceStore(this),
            new WalletCursorStore(this),
            Clock);

    public RealtimeWalletObservationService CreateWalletService() =>
        new(
            new AccountRepository(this),
            new PositionRepository(this),
            new PortfolioRepository(this),
            new StateStore(this),
            new RestOrderingEvidenceStore(this),
            new WalletCursorStore(this),
            new Transaction(this),
            new Outbox(this),
            Clock);

    public ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IExchangeAccountRepository>(new AccountRepository(this));
        services.AddSingleton(CreateCredentialStore());
        services.AddSingleton<IPrivateAccountProviderFactory>(new ProviderFactory(Provider));
        services.AddSingleton<IPositionRepository>(new PositionRepository(this));
        services.AddSingleton<IPortfolioStateRepository>(new PortfolioRepository(this));
        services.AddSingleton<IPositionObservationCursorStore>(new CursorStore(this));
        services.AddSingleton<IRealtimeObservationStateStore>(new StateStore(this));
        services.AddSingleton<IExchangeRestOrderingEvidenceStore>(new RestOrderingEvidenceStore(this));
        services.AddSingleton<IWalletObservationCursorStore>(new WalletCursorStore(this));
        services.AddSingleton<IExchangeAccountSyncTransaction>(new Transaction(this));
        services.AddSingleton<IApplicationEventOutbox>(new Outbox(this));
        services.AddApplication();
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static OpenPosition OpenPosition(
        string symbol = "BTCUSDT",
        decimal size = 1m,
        PositionSide side = PositionSide.Long,
        int positionIdx = 0,
        decimal? stopLoss = null,
        decimal? markPrice = 100m,
        SettlementAsset? settlementAsset = null) =>
        new(
            symbol,
            MarketCategory.Linear,
            side,
            PositionStatus.Normal,
            size,
            100m,
            size * 100m,
            2m,
            markPrice,
            100m,
            null,
            0m,
            null,
            stopLoss,
            null,
            1,
            null,
            null,
            null,
            positionIdx,
            settlementAsset ?? Usdt);

    public PrivatePositionObservation OpenObservation(
        DateTimeOffset at,
        long? sequence,
        OpenPosition? position = null)
    {
        position ??= OpenPosition();
        return new PrivatePositionObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = AccountId,
            Kind = PrivatePositionObservationKind.Open,
            Symbol = position.Symbol,
            Category = MarketCategory.Linear,
            PositionIdx = position.PositionIdx,
            Side = position.Side,
            Size = position.Size,
            ProviderSequence = sequence,
            ProviderUpdatedAt = at,
            MessageCreatedAt = at,
            ReceivedAt = at.AddMilliseconds(50),
            SettlementAsset = Usdt,
            Position = position,
        };
    }

    public PrivatePositionObservation CloseObservation(
        DateTimeOffset at,
        long? sequence,
        string symbol = "BTCUSDT",
        int positionIdx = 0) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = AccountId,
            Kind = PrivatePositionObservationKind.PotentialClose,
            Symbol = symbol,
            Category = MarketCategory.Linear,
            PositionIdx = positionIdx,
            Side = PositionSide.Unknown,
            Size = 0m,
            ProviderSequence = sequence,
            ProviderUpdatedAt = at,
            MessageCreatedAt = at,
            ReceivedAt = at.AddMilliseconds(50),
            SettlementAsset = Usdt,
        };

    public PrivateWalletObservation WalletObservation(
        DateTimeOffset at,
        decimal? totalEquity = 1_000m,
        decimal? available = 800m,
        decimal? walletBalance = 950m,
        decimal? unrealizedPnl = 50m) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = AccountId,
            Kind = PrivateWalletObservationKind.PartialUpdate,
            Balances = [new AccountBalance(AccountType.Unified, totalEquity, walletBalance, available, unrealizedPnl, [])],
            MessageCreatedAt = at,
            ReceivedAt = at.AddMilliseconds(50),
        };

    private IExchangeAccountCredentialStore CreateCredentialStore()
    {
        var store = new Mock<IExchangeAccountCredentialStore>();
        store
            .Setup(credentials => credentials.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => CredentialsUnavailable
                ? Task.FromException<ExchangeAccountCredential?>(
                    new ExchangeAccountCredentialsUnavailableException())
                : Task.FromResult<ExchangeAccountCredential?>(CredentialsMissing
                    ? null
                    : new ExchangeAccountCredential(
                        new ExchangeAccountCredentialSecret("test-key", "test-secret"),
                        ConcurrencyVersion.Initial)));
        return store.Object;
    }

    private static ExchangeAccount Clone(ExchangeAccount source) =>
        ExchangeAccount.Create(
            source.Id,
            source.UserId,
            source.ExchangeId,
            source.ProviderIdentity,
            source.DisplayName,
            source.ConnectionStatus,
            source.Capabilities,
            source.LastSyncedAt,
            source.LastError,
            source.LastAppliedBalanceObservationAt,
            source.LastAppliedPositionsObservationAt);

    private static Position Clone(Position source) =>
        Position.Restore(
            source.Id,
            source.ExchangePositionKey,
            source.MarketCategory,
            source.SettlementAsset,
            source.Size,
            source.FirstDetectedAt,
            source.LastObservedAt,
            source.TrackingState,
            source.ClosedAt,
            source.Changes,
            source.AverageEntryPrice,
            source.PositionValue,
            source.Leverage,
            source.MarkPrice,
            source.BreakEvenPrice,
            source.LiquidationPrice,
            source.UnrealizedPnl,
            source.TakeProfit,
            source.StopLoss,
            source.TrailingStop);

    private void EnsureScope(UserId userId, ExchangeAccountId exchangeAccountId)
    {
        if (userId != UserId || exchangeAccountId != AccountId)
        {
            throw new InvalidOperationException("Unexpected user or account scope.");
        }
    }

    internal sealed class MutableClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    internal sealed class FakePrivateProvider(RealtimeTestWorld world) : IPrivateAccountProvider
    {
        private readonly Dictionary<string, OpenPosition> exchangePositions = new(StringComparer.Ordinal);

        public OpenPositionsObservationStatus Status { get; set; } = OpenPositionsObservationStatus.Complete;

        public OpenPositionsObservation? PositionsObservationOverride { get; set; }

        public AccountBalance Balance { get; set; } =
            new(AccountType.Unified, 2_000m, 1_900m, 1_500m, 100m, []);

        /// <summary>Отставание момента REST-снимка позиций от часов backend.</summary>
        public TimeSpan SnapshotLag { get; set; }

        /// <summary>Момент REST-снимка позиций вместо часов backend, включая неинициализированный.</summary>
        public DateTimeOffset? SnapshotObservedAt { get; set; }

        /// <summary>REST-запрос баланса завершается ошибкой биржи.</summary>
        public bool BalanceFails { get; set; }

        public List<string?> PositionRequests { get; } = [];

        /// <summary>Выполняется во время REST-запроса позиций, до формирования ответа.</summary>
        public Action? OnPositionsRequest { get; set; }

        /// <summary>Асинхронно выполняется во время REST-запроса позиций, до формирования ответа.</summary>
        public Func<CancellationToken, Task>? OnPositionsRequestAsync { get; set; }

        /// <summary>Выполняется во время REST-запроса баланса, после фиксации начала чтения.</summary>
        public Action? OnBalanceRequest { get; set; }

        /// <summary>Асинхронно выполняется во время REST-запроса баланса, после фиксации начала чтения.</summary>
        public Func<CancellationToken, Task>? OnBalanceRequestAsync { get; set; }

        /// <summary>Исключение, которым завершается следующий REST-запрос позиций.</summary>
        public Exception? PositionsException { get; set; }

        /// <summary>
        /// Точный REST-снимок, включая идентичные и противоречащие дубликаты одного ключа.
        /// </summary>
        private List<OpenPosition>? exchangePositionsSnapshot;

        public void SetExchangePosition(OpenPosition position)
        {
            exchangePositionsSnapshot = null;
            exchangePositions[ExchangePositionStorageKey(position)] = position;
        }

        /// <summary>
        /// Задаёт REST-строки как есть (порядок и дубликаты сохраняются) для регрессий неоднозначности.
        /// </summary>
        public void ReplaceExchangePositions(params OpenPosition[] positions)
        {
            exchangePositions.Clear();
            exchangePositionsSnapshot = positions.ToList();
        }

        public void RemoveExchangePosition(string symbol, int positionIdx = 0, PositionSide? side = null)
        {
            exchangePositionsSnapshot = null;
            if (side is { } concreteSide)
            {
                exchangePositions.Remove($"{symbol}/{positionIdx}/{concreteSide}");
                return;
            }

            foreach (var key in exchangePositions.Keys
                         .Where(candidate => candidate.StartsWith($"{symbol}/{positionIdx}/", StringComparison.Ordinal))
                         .ToArray())
            {
                exchangePositions.Remove(key);
            }
        }

        public void ClearExchangePositions()
        {
            exchangePositions.Clear();
            exchangePositionsSnapshot = null;
        }

        private static string ExchangePositionStorageKey(OpenPosition position) =>
            $"{position.Symbol}/{position.PositionIdx}/{position.Side}";

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            PositionRequests.Add(symbol);
            if (PositionsException is { } exception)
            {
                throw exception;
            }

            var observedAt = SnapshotObservedAt ?? world.Clock.GetUtcNow() - SnapshotLag;
            OnPositionsRequest?.Invoke();
            if (OnPositionsRequestAsync is { } callback)
            {
                await callback(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (PositionsObservationOverride is { } observationOverride)
            {
                return observationOverride;
            }

            var source = exchangePositionsSnapshot ?? exchangePositions.Values.ToList();
            var observed = source
                .Where(position => symbol is null || position.Symbol == symbol)
                .ToArray();
            return Status switch
            {
                OpenPositionsObservationStatus.Complete =>
                    OpenPositionsObservation.Complete(category, symbol, observedAt, observed),
                OpenPositionsObservationStatus.Partial =>
                    OpenPositionsObservation.Partial(category, symbol, observedAt, observed, "partial"),
                _ => OpenPositionsObservation.Failed(category, symbol, observedAt, "failed"),
            };
        }

        public async Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default)
        {
            var requestStartedAt = world.Clock.GetUtcNow();
            OnBalanceRequest?.Invoke();
            if (OnBalanceRequestAsync is { } callback)
            {
                await callback(cancellationToken);
            }

            var observedAt = world.Clock.GetUtcNow();
            return BalanceFails
                ? AccountBalanceObservation.Failed(
                    new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false),
                    observedAt,
                    requestStartedAt)
                : AccountBalanceObservation.Complete(Balance, observedAt, requestStartedAt);
        }
    }

    private sealed class ProviderFactory(IPrivateAccountProvider provider) : IPrivateAccountProviderFactory
    {
        public IPrivateAccountProviderLease Create(ExchangeId exchange, ExchangeAccountCredential credentials) =>
            new Lease(provider);

        private sealed class Lease(IPrivateAccountProvider provider) : IPrivateAccountProviderLease
        {
            public IPrivateAccountProvider Provider => provider;

            public void Dispose()
            {
            }
        }
    }

    private sealed class Transaction(RealtimeTestWorld world) : IExchangeAccountSyncTransaction
    {
        public Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            world.TransactionAttempts++;
            var accountSnapshot = world.account;
            var positionsSnapshot = new Dictionary<PositionId, Versioned<Position>>(world.positions);
            var portfoliosSnapshot = world.portfolios.ToList();
            var cursorsSnapshot = new Dictionary<PositionObservationCursorKey, Versioned<PositionObservationCursor>>(world.cursors);
            var eventsSnapshot = world.events.ToList();
            var stateSnapshot = world.state;
            var restOrderingSnapshot = world.restOrderingIntervals.ToList();
            var walletCursorSnapshot = world.lastAcceptedWalletMessageCreatedAt;
            try
            {
                if (world.BeforeTransactionExecuteAsync is { } beforeExecute)
                {
                    await beforeExecute(cancellationToken);
                }

                await operation(cancellationToken);
                if (world.CommitFailuresToInject > 0)
                {
                    world.CommitFailuresToInject--;
                    throw new InvalidOperationException("Injected commit failure.");
                }
            }
            catch
            {
                world.account = accountSnapshot;
                world.positions = positionsSnapshot;
                world.portfolios = portfoliosSnapshot;
                world.cursors = cursorsSnapshot;
                world.events = eventsSnapshot;
                world.state = stateSnapshot;
                world.restOrderingIntervals = restOrderingSnapshot;
                world.lastAcceptedWalletMessageCreatedAt = walletCursorSnapshot;
                throw;
            }
        }
    }

    private sealed class WalletCursorStore(RealtimeTestWorld world) : IWalletObservationCursorStore
    {
        public Task<DateTimeOffset?> GetLastAcceptedMessageCreatedAtAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                return Task.FromResult<DateTimeOffset?>(null);
            }

            return Task.FromResult(world.lastAcceptedWalletMessageCreatedAt);
        }

        public Task SaveAcceptedAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            DateTimeOffset messageCreatedAt,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                throw new InvalidOperationException(
                    "The exchange account is unavailable in the user scope for the wallet observation cursor.");
            }

            var canonical = TimestampCanonicalizer.ToUtcMicroseconds(messageCreatedAt);
            if (world.lastAcceptedWalletMessageCreatedAt is { } current && current > canonical)
            {
                throw new InvalidOperationException(
                    "The wallet observation cursor cannot regress to an older exchange MessageCreatedAt.");
            }

            world.lastAcceptedWalletMessageCreatedAt = canonical;
            return Task.CompletedTask;
        }
    }

    private sealed class RestOrderingEvidenceStore(RealtimeTestWorld world) : IExchangeRestOrderingEvidenceStore
    {
        public Task AppendAsync(
            UserId userId,
            ExchangeRestOrderingInterval interval,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(interval);
            if (userId != world.UserId || interval.ExchangeAccountId != world.AccountId)
            {
                throw new InvalidOperationException(
                    "The exchange account is unavailable in the user scope for REST ordering evidence.");
            }

            if (interval.StartedAt > interval.CompletedAt)
            {
                throw new ArgumentException("REST ordering interval requires StartedAt <= CompletedAt.");
            }

            world.restOrderingIntervals.Add(interval);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetPositionEvidenceAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            string symbol,
            DateTimeOffset observedAt,
            DateTimeOffset? exchangeFactAt,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                return Task.FromResult<IReadOnlyList<ExchangeRestOrderingInterval>>([]);
            }

            var matches = world.restOrderingIntervals
                .Where(interval =>
                    RestOrderingDecision.PositionOverlapsRest(observedAt, symbol, [interval], exchangeFactAt))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ExchangeRestOrderingInterval>>(matches);
        }

        public Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetBalanceEvidenceAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            DateTimeOffset? observedAt,
            DateTimeOffset? messageCreatedAt,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                return Task.FromResult<IReadOnlyList<ExchangeRestOrderingInterval>>([]);
            }

            var matches = world.restOrderingIntervals
                .Where(interval => RestOrderingDecision.WalletOverlapsBalanceRead(observedAt, messageCreatedAt, [interval]))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ExchangeRestOrderingInterval>>(matches);
        }

        public Task<bool> HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            string symbol,
            DateTimeOffset acceptedRestObservationAt,
            DateTimeOffset streamObservedAt,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                return Task.FromResult(false);
            }

            var trimmedSymbol = symbol.Trim();
            var completedBefore = world.restOrderingIntervals.Any(interval =>
                interval.StartedAt == acceptedRestObservationAt &&
                interval.CompletedAt < streamObservedAt &&
                (interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete ||
                 (interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                  string.Equals(interval.Symbol, trimmedSymbol, StringComparison.OrdinalIgnoreCase))));
            return Task.FromResult(completedBefore);
        }

        public Task<bool> HasBalanceEvidenceCompletingAtAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId || exchangeAccountId != world.AccountId)
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(world.restOrderingIntervals.Any(interval =>
                interval.Kind == ExchangeRestOrderingKind.Balance &&
                interval.CompletedAt == completedAt));
        }
    }

    private sealed class Outbox(RealtimeTestWorld world) : IApplicationEventOutbox
    {
        public Task AddAsync(IApplicationEvent applicationEvent, CancellationToken cancellationToken = default)
        {
            world.events.Add(applicationEvent);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(
            IReadOnlyCollection<IApplicationEvent> applicationEvents,
            CancellationToken cancellationToken = default)
        {
            world.events.AddRange(applicationEvents);
            return Task.CompletedTask;
        }
    }

    private sealed class AccountRepository(RealtimeTestWorld world) : IExchangeAccountRepository
    {
        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListActiveAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Versioned<ExchangeAccount>?> GetByIdAsync(
            UserId userId,
            ExchangeAccountId id,
            CancellationToken cancellationToken = default)
        {
            // Как и PostgreSQL-репозиторий, чужой или неизвестный аккаунт не находится.
            return Task.FromResult(userId == world.UserId && id == world.AccountId && world.account is { } current
                ? new Versioned<ExchangeAccount>(Clone(current.Value), current.Version)
                : null);
        }

        public Task<Versioned<ExchangeAccount>?> GetByProviderIdentityAsync(
            UserId userId,
            ExchangeId exchangeId,
            ExchangeAccountProviderIdentity providerIdentity,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            ExchangeAccount account,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, account.Id);
            if (world.AccountSaveConflictsToInject > 0)
            {
                world.AccountSaveConflictsToInject--;
                throw new ConcurrencyConflictException("Injected account conflict.");
            }

            if (world.account is not { } current || expectedVersion != current.Version)
            {
                throw new ConcurrencyConflictException("Account version conflict.");
            }

            var next = current.Version.Next();
            world.account = new Versioned<ExchangeAccount>(Clone(account), next);
            return Task.FromResult(next);
        }

        public Task DeleteAsync(
            UserId userId,
            ExchangeAccountId id,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PositionRepository(RealtimeTestWorld world) : IPositionRepository
    {
        public Task<IReadOnlyCollection<Versioned<Position>>> GetByExchangeAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            IReadOnlyCollection<Versioned<Position>> result = world.positions.Values
                .OrderBy(versioned => versioned.Value.Id.Value)
                .Select(versioned => new Versioned<Position>(Clone(versioned.Value), versioned.Version))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<PositionVersionWatermark>> GetCurrentVersionWatermarksAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            IReadOnlyCollection<PositionVersionWatermark> result = world.positions.Values
                .Where(versioned => versioned.Value.TrackingState != PositionTrackingState.Closed)
                .Select(versioned => new PositionVersionWatermark(versioned.Value.Id, versioned.Version))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<PositionPortfolioProjection>> GetCurrentPortfolioPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            IReadOnlyCollection<PositionPortfolioProjection> result = world.positions.Values
                .Where(versioned => versioned.Value.TrackingState != PositionTrackingState.Closed)
                .Select(PositionPortfolioProjection.From)
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<Versioned<Position>>> GetCurrentWithHistoryAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            IReadOnlyCollection<Versioned<Position>> result = world.positions.Values
                .Where(versioned => versioned.Value.TrackingState != PositionTrackingState.Closed)
                .OrderBy(versioned => versioned.Value.Id.Value)
                .Select(versioned => new Versioned<Position>(Clone(versioned.Value), versioned.Version))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyCollection<LatestClosedPositionSlot>> GetLatestClosedSlotsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            MarketCategory marketCategory,
            IReadOnlyCollection<PositionSlotKey> slots,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            world.LastRequestedClosedSlots = slots.ToArray();
            IReadOnlyCollection<LatestClosedPositionSlot> result = slots
                .Select(slot => world.positions.Values
                    .Select(versioned => versioned.Value)
                    .Where(position =>
                        position.TrackingState == PositionTrackingState.Closed &&
                        position.MarketCategory == marketCategory &&
                        position.ExchangePositionKey.PositionIdx == slot.PositionIdx &&
                        string.Equals(
                            position.ExchangePositionKey.InstrumentId.Value,
                            slot.InstrumentId.Value,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(position => position.ClosedAt)
                    .Max())
                .Select((closedAt, index) => closedAt is { } value
                    ? new LatestClosedPositionSlot(slots.ElementAt(index), value)
                    : null)
                .OfType<LatestClosedPositionSlot>()
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<Versioned<Position>?> GetByIdAsync(
            UserId userId,
            PositionId id,
            CancellationToken cancellationToken = default)
        {
            if (userId != world.UserId ||
                !world.positions.TryGetValue(id, out var position) ||
                position.Value.ExchangePositionKey.ExchangeAccountId != world.AccountId)
            {
                return Task.FromResult<Versioned<Position>?>(null);
            }

            return Task.FromResult<Versioned<Position>?>(
                new Versioned<Position>(Clone(position.Value), position.Version));
        }

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            Position position,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, position.ExchangePositionKey.ExchangeAccountId);
            world.positions.TryGetValue(position.Id, out var current);
            if (expectedVersion is null)
            {
                if (current is not null ||
                    (position.TrackingState != PositionTrackingState.Closed &&
                     world.ActivePositions.Any(active => active.ExchangePositionKey == position.ExchangePositionKey)))
                {
                    throw new ConcurrencyConflictException("Position insert conflict.");
                }

                world.positions[position.Id] = new Versioned<Position>(Clone(position), ConcurrencyVersion.Initial);
                return Task.FromResult(ConcurrencyVersion.Initial);
            }

            if (current is null || current.Version != expectedVersion)
            {
                throw new ConcurrencyConflictException("Position version conflict.");
            }

            var next = current.Version.Next();
            world.positions[position.Id] = new Versioned<Position>(Clone(position), next);
            return Task.FromResult(next);
        }
    }

    private sealed class PortfolioRepository(RealtimeTestWorld world) : IPortfolioStateRepository
    {
        public Task<PortfolioState?> GetLatestAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            // Как PostgreSQL-репозиторий, последней считается сводка с наибольшим CalculatedAt.
            return Task.FromResult(world.portfolios
                .Select((state, index) => (State: state, Index: index))
                .OrderByDescending(entry => entry.State.CalculatedAt)
                .ThenByDescending(entry => entry.Index)
                .Select(entry => entry.State)
                .FirstOrDefault());
        }

        public Task SaveAsync(UserId userId, PortfolioState state, CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, state.ExchangeAccountId);
            world.portfolios.Add(state);
            return Task.CompletedTask;
        }
    }

    private sealed class CursorStore(RealtimeTestWorld world) : IPositionObservationCursorStore
    {
        public Task<Versioned<PositionObservationCursor>?> GetAsync(
            UserId userId,
            PositionObservationCursorKey key,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, key.ExchangeAccountId);
            return Task.FromResult(world.cursors.TryGetValue(key, out var cursor) ? cursor : null);
        }

        public Task<IReadOnlyCollection<Versioned<PositionObservationCursor>>> GetByExchangeAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            PositionObservationCursor cursor,
            ConcurrencyVersion? expectedVersion,
            long expectedGeneration,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, cursor.Key.ExchangeAccountId);
            if (world.CursorConflictsToInject > 0)
            {
                world.CursorConflictsToInject--;
                throw new ConcurrencyConflictException("Injected cursor conflict.");
            }

            world.cursors.TryGetValue(cursor.Key, out var current);
            if (world.state?.Value.Generation != expectedGeneration ||
                current?.Version != expectedVersion ||
                current is not null &&
                (cursor.LastProviderSequence < current.Value.LastProviderSequence ||
                 current.Value.LastProviderSequence is not null && cursor.LastProviderSequence is null ||
                 cursor.LastProviderUpdatedAt < current.Value.LastProviderUpdatedAt ||
                 current.Value.LastProviderUpdatedAt is not null && cursor.LastProviderUpdatedAt is null))
            {
                throw new ConcurrencyConflictException("Cursor conflict.");
            }

            var next = current?.Version.Next() ?? ConcurrencyVersion.Initial;
            world.cursors[cursor.Key] = new Versioned<PositionObservationCursor>(cursor, next);
            return Task.FromResult(next);
        }
    }

    private sealed class StateStore(RealtimeTestWorld world) : IRealtimeObservationStateStore
    {
        public Task<Versioned<RealtimeObservationState>?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.BeforeStateRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            // Как и PostgreSQL-хранилище, чужой или неизвестный аккаунт не имеет состояния.
            return Task.FromResult(userId == world.UserId && exchangeAccountId == world.AccountId ? world.state : null);
        }

        public Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            RealtimeObservationTransportStatus status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            RealtimeObservationState state,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, state.ExchangeAccountId);
            world.BeforeStateSave?.Invoke(state);
            cancellationToken.ThrowIfCancellationRequested();
            if (world.StateSaveFailuresToInject > 0)
            {
                world.StateSaveFailuresToInject--;
                throw new InvalidOperationException("Injected observation state persistence failure.");
            }

            if (world.StateConflictsToInject > 0)
            {
                world.StateConflictsToInject--;
                throw new ConcurrencyConflictException("Injected observation state conflict.");
            }

            if (world.state is not { } current ||
                current.Version != expectedVersion ||
                current.Value.Generation != state.Generation)
            {
                throw new ConcurrencyConflictException("State conflict.");
            }

            var next = current.Version.Next();
            world.state = new Versioned<RealtimeObservationState>(state, next);
            return Task.FromResult(next);
        }

        public Task<RealtimeObservationStateLockResult> TryLockGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            long generation,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            var state = world.state?.Value;
            var result = state?.Generation != generation
                ? RealtimeObservationStateLockResult.GenerationSuperseded
                : state.RecoveryRequired
                    ? RealtimeObservationStateLockResult.RecoveryRequired
                    : RealtimeObservationStateLockResult.Locked;
            return Task.FromResult(result);
        }
    }
}
