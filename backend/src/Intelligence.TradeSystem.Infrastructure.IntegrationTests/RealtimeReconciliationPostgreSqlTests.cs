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
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Application;
using Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql-A")]
public sealed class RealtimeReconciliationPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan BlockedCommandProbe = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);
    private static readonly SettlementAsset Usdt = SettlementAsset.From("USDT");

    [Fact]
    public async Task Stream_open_persists_position_cursor_portfolio_and_outbox_together()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        var result = await ApplyAsync(account, generation, OpenObservation(account, T1, 10));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        await using var context = fixture.CreateContext();
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(PositionTrackingState.Active, position.Value.TrackingState);
        Assert.Equal(T1, position.Value.LastObservedAt);
        Assert.Equal(1, await CountChangesAsync(context, position.Value.Id));
        var cursor = await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(10, cursor!.Value.LastProviderSequence);
        Assert.Equal(1, await CountPortfoliosAsync(context, account));
        Assert.Equal(1, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionOpened));
        Assert.Equal(1, await CountOutboxAsync(context, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(0, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionEvaluationUpdated));
    }

    [Fact]
    public async Task Failure_before_commit_rolls_back_position_cursor_portfolio_and_outbox()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            var service = CreateRealtimeService(
                context,
                new TestPrivateProvider(),
                new FailingBeforeCommitTransaction(new ExchangeAccountSyncTransaction(context)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplyAsync(account.UserId, generation, OpenObservation(account, T1, 1)));
        }

        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Generation_lock_requires_a_transaction_and_fences_generation_change_until_commit()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using var observerContext = fixture.CreateContext();
        await using var lifecycleContext = fixture.CreateContext();
        var observerStore = new RealtimeObservationStateStore(observerContext);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            observerStore.TryLockGenerationAsync(account.UserId, account.Id, generation));

        await using (var transaction = await observerContext.Database.BeginTransactionAsync())
        {
            Assert.False(await observerStore.TryLockGenerationAsync(account.UserId, account.Id, generation + 1));
            Assert.False(await observerStore.TryLockGenerationAsync(UserId.New(), account.Id, generation));
            Assert.True(await observerStore.TryLockGenerationAsync(account.UserId, account.Id, generation));

            var advance = new RealtimeObservationStateStore(lifecycleContext).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Reconnecting);
            Assert.NotSame(advance, await Task.WhenAny(advance, Task.Delay(BlockedCommandProbe)));

            await transaction.CommitAsync();
            Assert.Equal(generation + 1, (await advance.WaitAsync(CompletionTimeout)).Value.Generation);
        }
    }

    [Fact]
    public async Task Generation_change_committed_while_the_observation_waits_rejects_it_without_writes()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using var lifecycleContext = fixture.CreateContext();
        await using var observerContext = fixture.CreateContext();
        Task<RealtimeObservationApplyResult> apply;
        await using (var transaction = await lifecycleContext.Database.BeginTransactionAsync())
        {
            await new RealtimeObservationStateStore(lifecycleContext).AdvanceGenerationAsync(
                account.UserId,
                account.Id,
                RealtimeObservationTransportStatus.Reconnecting);

            apply = CreateRealtimeService(observerContext, new TestPrivateProvider())
                .ApplyAsync(account.UserId, generation, OpenObservation(account, T1, 1));
            Assert.NotSame(apply, await Task.WhenAny(apply, Task.Delay(BlockedCommandProbe)));

            await transaction.CommitAsync();
        }

        var result = await apply.WaitAsync(CompletionTimeout);
        Assert.Equal(RealtimeObservationApplyOutcome.GenerationSuperseded, result.Outcome);
        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Null(await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account)));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Stream_open_racing_with_manual_sync_keeps_one_lifecycle_without_duplicate_history()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        using var barrier = new Barrier(2);
        var restProvider = new TestPrivateProvider(barrier);
        restProvider.SetPosition(OpenPosition());
        await using var services = CreateApplicationServices(account, restProvider);

        var sync = Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IExchangeAccountSyncService>()
                .SynchronizeAsync(account.UserId, account.Id);
        });
        var stream = Task.Run(async () =>
        {
            barrier.SignalAndWait(CompletionTimeout);
            return await ApplyAsync(account, generation, OpenObservation(account, T1, 1));
        });
        var syncResult = await sync.WaitAsync(CompletionTimeout);
        var streamResult = await stream.WaitAsync(CompletionTimeout);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, syncResult.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, streamResult.Outcome);
        await using var context = fixture.CreateContext();
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(PositionTrackingState.Active, position.Value.TrackingState);
        Assert.Equal(1, await CountChangesAsync(context, position.Value.Id));
        Assert.Equal(1, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionOpened));
    }

    [Fact]
    public async Task Stream_close_racing_with_scheduled_sync_closes_the_lifecycle_once()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 1))).Outcome);
        using var barrier = new Barrier(2);
        var restProvider = new TestPrivateProvider(barrier) { ObservedAt = T1.AddSeconds(10) };
        await using var services = CreateApplicationServices(account, restProvider);
        var sweep = new ExchangeAccountBackgroundSyncSweep(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ExchangeAccountBackgroundSyncOptions { BatchSize = 1, MaxConcurrency = 1 }),
            TimeProvider.System,
            NullLogger<ExchangeAccountBackgroundSyncSweep>.Instance);

        var sync = Task.Run(() => sweep.RunAsync());
        var stream = Task.Run(async () =>
        {
            barrier.SignalAndWait(CompletionTimeout);
            return await ApplyAsync(
                account,
                generation,
                CloseObservation(account, T1.AddSeconds(5), 2),
                new TestPrivateProvider { ObservedAt = T1.AddSeconds(11) });
        });
        var sweepResult = await sync.WaitAsync(CompletionTimeout);
        var streamResult = await stream.WaitAsync(CompletionTimeout);

        Assert.Equal(0, sweepResult.UnexpectedFailureCount);
        Assert.Equal(1, Assert.Single(sweepResult.OutcomeCounts, entry => entry.Key == ExchangeAccountSyncOutcome.Synchronized).Value);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, streamResult.Outcome);
        await using var context = fixture.CreateContext();
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(PositionTrackingState.Closed, position.Value.TrackingState);
        Assert.Equal(1, await context.PositionChanges.CountAsync(change =>
            change.PositionId == position.Value.Id.Value && change.Kind == PositionChangeKind.Closed));
        Assert.Equal(1, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionClosed));
    }

    [Fact]
    public async Task Concurrent_observations_of_one_symbol_serialize_with_bounded_retries()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        var results = await Task.WhenAll(
                Task.Run(() => ApplyAsync(account, generation, OpenObservation(account, T1, 1))),
                Task.Run(() => ApplyAsync(
                    account,
                    generation,
                    OpenObservation(account, T1.AddSeconds(1), 2, OpenPosition(size: 2m)))))
            .WaitAsync(CompletionTimeout);

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, results[1].Outcome);
        Assert.Contains(results[0].Outcome, new[] { RealtimeObservationApplyOutcome.Applied, RealtimeObservationApplyOutcome.Stale });
        await using var context = fixture.CreateContext();
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(2m, position.Value.Size);
        Assert.Equal(position.Value.Changes.Count, await CountChangesAsync(context, position.Value.Id));
        var cursor = await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(2, cursor!.Value.LastProviderSequence);
    }

    [Fact]
    public async Task Concurrent_observations_of_different_symbols_do_not_deadlock()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        var results = await Task.WhenAll(
                Task.Run(() => ApplyAsync(account, generation, OpenObservation(account, T1, 1, OpenPosition("BTCUSDT")))),
                Task.Run(() => ApplyAsync(account, generation, OpenObservation(account, T1, 1, OpenPosition("ETHUSDT")))))
            .WaitAsync(CompletionTimeout);

        Assert.All(results, result => Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome));
        await using var context = fixture.CreateContext();
        Assert.Equal(2, (await new PositionRepository(context).GetByExchangeAccountAsync(account.UserId, account.Id)).Count);
        Assert.Equal(2, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionOpened));
    }

    private async Task<(ExchangeAccount Account, long Generation)> CreateObservedAccountAsync()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            "Основной",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        await using var context = fixture.CreateContext();
        await new ExchangeAccountRepository(context).SaveAsync(account.UserId, account, expectedVersion: null);
        var state = await new RealtimeObservationStateStore(context).AdvanceGenerationAsync(
            account.UserId,
            account.Id,
            RealtimeObservationTransportStatus.Connected);
        return (account, state.Value.Generation);
    }

    private async Task<RealtimeObservationApplyResult> ApplyAsync(
        ExchangeAccount account,
        long generation,
        PrivatePositionObservation observation,
        TestPrivateProvider? provider = null)
    {
        await using var context = fixture.CreateContext();
        return await CreateRealtimeService(context, provider ?? new TestPrivateProvider())
            .ApplyAsync(account.UserId, generation, observation);
    }

    /// <summary>
    /// Production-регистрация Application поверх PostgreSQL-репозиториев: REST-синхронизации проходят
    /// через тот же coordinator, что и в API и фоновой синхронизации.
    /// </summary>
    private ServiceProvider CreateApplicationServices(ExchangeAccount account, TestPrivateProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(T1.AddMinutes(1)));
        services.AddScoped(_ => fixture.CreateContext());
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPositionObservationCursorStore, PositionObservationCursorStore>();
        services.AddScoped<IRealtimeObservationStateStore, RealtimeObservationStateStore>();
        services.AddScoped<IExchangeAccountSyncTransaction, ExchangeAccountSyncTransaction>();
        services.AddScoped<IApplicationEventOutbox>(serviceProvider =>
            new ApplicationEventOutbox(serviceProvider.GetRequiredService<TradeSystemDbContext>()));
        services.AddSingleton<IExchangeAccountCredentialStore>(new TestCredentialStore());
        services.AddSingleton<IPrivateAccountProviderFactory>(new TestPrivateProviderFactory(provider));
        services.AddSingleton<IExchangeAccountSyncCandidateSource>(new SingleCandidateSource(account));
        services.AddApplication();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static RealtimePositionReconciliationService CreateRealtimeService(
        TradeSystemDbContext context,
        TestPrivateProvider provider,
        IExchangeAccountSyncTransaction? transaction = null) =>
        new(
            new ExchangeAccountRepository(context),
            new TestCredentialStore(),
            new TestPrivateProviderFactory(provider),
            new PositionRepository(context),
            new PortfolioStateRepository(context),
            new PositionObservationCursorStore(context),
            new RealtimeObservationStateStore(context),
            transaction ?? new ExchangeAccountSyncTransaction(context),
            new ApplicationEventOutbox(context),
            new FixedTimeProvider(T1.AddMinutes(1)));

    private static PositionObservationCursorKey CursorKey(ExchangeAccount account, string symbol = "BTCUSDT") =>
        PositionObservationCursorKey.Create(account.Id, MarketCategory.Linear, InstrumentId.From(symbol), 0);

    private static Task<int> CountChangesAsync(TradeSystemDbContext context, PositionId positionId) =>
        context.PositionChanges.CountAsync(change => change.PositionId == positionId.Value);

    private static Task<int> CountPortfoliosAsync(TradeSystemDbContext context, ExchangeAccount account) =>
        context.PortfolioStates.CountAsync(state => state.ExchangeAccountId == account.Id.Value);

    private static async Task<int> CountOutboxAsync(
        TradeSystemDbContext context,
        ExchangeAccount account,
        string? eventType = null)
    {
        var accountId = account.Id.Value.ToString();
        var payloads = await context.OutboxMessages
            .Where(message => eventType == null || message.EventType == eventType)
            .Select(message => message.Payload)
            .ToArrayAsync();
        return payloads.Count(payload => payload.Contains(accountId, StringComparison.Ordinal));
    }

    private static OpenPosition OpenPosition(string symbol = "BTCUSDT", decimal size = 1m) =>
        new(
            symbol,
            MarketCategory.Linear,
            PositionSide.Long,
            PositionStatus.Normal,
            size,
            100m,
            size * 100m,
            2m,
            100m,
            null,
            null,
            0m,
            null,
            null,
            null,
            1,
            null,
            null,
            null,
            0,
            Usdt);

    private static PrivatePositionObservation OpenObservation(
        ExchangeAccount account,
        DateTimeOffset at,
        long sequence,
        OpenPosition? position = null)
    {
        position ??= OpenPosition();
        return new PrivatePositionObservation
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = account.Id,
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

    private static PrivatePositionObservation CloseObservation(ExchangeAccount account, DateTimeOffset at, long sequence) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = account.Id,
            Kind = PrivatePositionObservationKind.PotentialClose,
            Symbol = "BTCUSDT",
            Category = MarketCategory.Linear,
            PositionIdx = 0,
            Side = PositionSide.Unknown,
            Size = 0m,
            ProviderSequence = sequence,
            ProviderUpdatedAt = at,
            MessageCreatedAt = at,
            ReceivedAt = at.AddMilliseconds(50),
            SettlementAsset = Usdt,
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class SingleCandidateSource(ExchangeAccount account) : IExchangeAccountSyncCandidateSource
    {
        public Task<IReadOnlyList<ExchangeAccountSyncCandidate>> GetBatchAsync(
            ExchangeAccountId? after,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExchangeAccountSyncCandidate>>(
                after is null ? [new ExchangeAccountSyncCandidate(account.UserId, account.Id, null)] : []);
    }

    private sealed class FailingBeforeCommitTransaction(IExchangeAccountSyncTransaction inner)
        : IExchangeAccountSyncTransaction
    {
        public Task LockPositionsAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.LockPositionsAsync(userId, exchangeAccountId, cancellationToken);

        public Task LockAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.LockAccountAsync(userId, exchangeAccountId, cancellationToken);

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(
                async token =>
                {
                    await operation(token);
                    throw new InvalidOperationException("Injected failure after outbox write.");
                },
                cancellationToken);
    }

    private sealed class TestCredentialStore : IExchangeAccountCredentialStore
    {
        private static readonly ExchangeAccountCredential Credential =
            new(new ExchangeAccountCredentialSecret("integration-key", "integration-secret"), ConcurrencyVersion.Initial);

        public Task<ExchangeAccountCredential?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ExchangeAccountCredential?>(Credential);

        public Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ExchangeAccountCredentialMetadata?>(null);

        public Task<ConcurrencyVersion> CreateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ExchangeAccountCredentialSecret secret,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> RotateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            ExchangeAccountCredentialSecret replacement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> ReprotectAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestPrivateProviderFactory(TestPrivateProvider provider) : IPrivateAccountProviderFactory
    {
        public IPrivateAccountProviderLease Create(ExchangeId exchange, ExchangeAccountCredential credentials) =>
            new Lease(provider);

        private sealed class Lease(TestPrivateProvider provider) : IPrivateAccountProviderLease
        {
            public IPrivateAccountProvider Provider => provider;

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// REST-провайдер с необязательным барьером: запрос позиций ждёт параллельную операцию,
    /// чтобы она выполнялась между получением снимка и его сохранением.
    /// </summary>
    private sealed class TestPrivateProvider(Barrier? positionsBarrier = null) : IPrivateAccountProvider
    {
        private readonly List<OpenPosition> positions = [];

        public DateTimeOffset ObservedAt { get; init; } = T1;

        public void SetPosition(OpenPosition position) => positions.Add(position);

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
                ObservedAt));

        public Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            positionsBarrier?.SignalAndWait(CompletionTimeout, cancellationToken);
            return Task.FromResult(OpenPositionsObservation.Complete(
                category,
                symbol,
                ObservedAt,
                positions.Where(position => symbol is null || position.Symbol == symbol).ToArray()));
        }
    }
}
