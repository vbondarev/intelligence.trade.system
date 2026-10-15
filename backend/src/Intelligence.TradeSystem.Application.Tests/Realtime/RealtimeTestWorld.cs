using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.State;
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

    public RealtimeTestWorld()
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
        state = new Versioned<RealtimeObservationState>(
            new RealtimeObservationState
            {
                ExchangeAccountId = AccountId,
                Generation = 1,
                TransportStatus = RealtimeObservationTransportStatus.Connected,
                RecoveryRequired = true,
            },
            ConcurrencyVersion.Initial);
        Provider = new FakePrivateProvider(this);
    }

    public UserId UserId { get; } = UserId.New();

    public ExchangeAccountId AccountId { get; } = ExchangeAccountId.New();

    public MutableClock Clock { get; } = new(T0.AddMinutes(1));

    public FakePrivateProvider Provider { get; }

    /// <summary>Количество оставшихся искусственных CAS-конфликтов при сохранении курсора.</summary>
    public int CursorConflictsToInject { get; set; }

    public int TransactionAttempts { get; private set; }

    public IReadOnlyList<IApplicationEvent> Events => events;

    public IReadOnlyList<PortfolioState> Portfolios => portfolios;

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

    public void AdvanceGeneration()
    {
        var current = state!;
        state = new Versioned<RealtimeObservationState>(
            current.Value with { Generation = current.Value.Generation + 1, RecoveryRequired = true },
            current.Version.Next());
    }

    public void DisableAccount()
    {
        var current = account!;
        current.Value.Disable();
        account = new Versioned<ExchangeAccount>(Clone(current.Value), current.Version.Next());
    }

    public void SeedPosition(Position position) =>
        positions[position.Id] = new Versioned<Position>(Clone(position), ConcurrencyVersion.Initial);

    public void SetPositionsWatermark(DateTimeOffset observedAt)
    {
        var current = account!;
        var updated = Clone(current.Value);
        updated.AdvanceObservationWatermark(ExchangeAccountObservationResource.Positions, observedAt);
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
            new Transaction(this),
            new Outbox(this),
            Clock);

    public RealtimeWalletObservationService CreateWalletService() =>
        new(
            new AccountRepository(this),
            new PositionRepository(this),
            new PortfolioRepository(this),
            new StateStore(this),
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
        decimal? markPrice = 100m) =>
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
            Usdt);

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

    private static IExchangeAccountCredentialStore CreateCredentialStore()
    {
        var store = new Mock<IExchangeAccountCredentialStore>();
        store
            .Setup(credentials => credentials.GetAsync(
                It.IsAny<UserId>(),
                It.IsAny<ExchangeAccountId>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeAccountCredential(
                new ExchangeAccountCredentialSecret("test-key", "test-secret"),
                ConcurrencyVersion.Initial));
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

        public AccountBalance Balance { get; set; } =
            new(AccountType.Unified, 2_000m, 1_900m, 1_500m, 100m, []);

        /// <summary>Отставание момента REST-снимка позиций от часов backend.</summary>
        public TimeSpan SnapshotLag { get; set; }

        /// <summary>Момент REST-снимка позиций вместо часов backend, включая неинициализированный.</summary>
        public DateTimeOffset? SnapshotObservedAt { get; set; }
        public List<string?> PositionRequests { get; } = [];

        /// <summary>Выполняется во время REST-запроса позиций, до формирования ответа.</summary>
        public Action? OnPositionsRequest { get; set; }

        public void SetExchangePosition(OpenPosition position) =>
            exchangePositions[$"{position.Symbol}/{position.PositionIdx}"] = position;

        public void RemoveExchangePosition(string symbol, int positionIdx = 0) =>
            exchangePositions.Remove($"{symbol}/{positionIdx}");

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            PositionRequests.Add(symbol);
            var observedAt = SnapshotObservedAt ?? world.Clock.GetUtcNow() - SnapshotLag;
            OnPositionsRequest?.Invoke();
            var observed = exchangePositions.Values
                .Where(position => symbol is null || position.Symbol == symbol)
                .ToArray();
            return Task.FromResult(Status switch
            {
                OpenPositionsObservationStatus.Complete =>
                    OpenPositionsObservation.Complete(category, symbol, observedAt, observed),
                OpenPositionsObservationStatus.Partial =>
                    OpenPositionsObservation.Partial(category, symbol, observedAt, observed, "partial"),
                _ => OpenPositionsObservation.Failed(category, symbol, observedAt, "failed"),
            });
        }

        public Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountBalanceObservation.Complete(Balance, world.Clock.GetUtcNow()));
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
            try
            {
                await operation(cancellationToken);
            }
            catch
            {
                world.account = accountSnapshot;
                world.positions = positionsSnapshot;
                world.portfolios = portfoliosSnapshot;
                world.cursors = cursorsSnapshot;
                world.events = eventsSnapshot;
                world.state = stateSnapshot;
                throw;
            }
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
            world.EnsureScope(userId, id);
            return Task.FromResult(world.account is { } current
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

        public Task<IReadOnlyCollection<PositionVersionWatermark>> GetVersionWatermarkByExchangeAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            IReadOnlyCollection<PositionVersionWatermark> result = world.positions.Values
                .Select(versioned => new PositionVersionWatermark(versioned.Value.Id, versioned.Version))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<Versioned<Position>?> GetByIdAsync(
            UserId userId,
            PositionId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
            return Task.FromResult(world.portfolios.LastOrDefault());
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
            world.EnsureScope(userId, exchangeAccountId);
            return Task.FromResult(world.state);
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

        public Task<bool> TryLockGenerationAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            long generation,
            CancellationToken cancellationToken = default)
        {
            world.EnsureScope(userId, exchangeAccountId);
            return Task.FromResult(world.state?.Value.Generation == generation);
        }
    }
}
