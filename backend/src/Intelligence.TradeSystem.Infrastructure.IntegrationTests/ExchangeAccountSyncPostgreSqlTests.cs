using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class ExchangeAccountSyncPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);
    private static readonly ExchangeAccountCapabilities Capabilities =
        ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions;

    [Fact]
    public async Task Concurrent_same_observation_converges_to_one_position_history_and_portfolio()
    {
        var account = CreateAccount();
        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext)
                .SaveAsync(account.UserId, account, expectedVersion: null);
        }

        using var fetchBarrier = new Barrier(2);
        var observation = CreateObservation(T1, size: 1m);
        var providers = new[]
        {
            new TestPrivateProvider(observation, fetchBarrier),
            new TestPrivateProvider(observation, fetchBarrier),
        };

        var first = RunSynchronization(account, providers[0], T1);
        var second = RunSynchronization(account, providers[1], T1);
        var results = await Task.WhenAll(first, second);

        Assert.Contains(results, result => result.Outcome == ExchangeAccountSyncOutcome.Synchronized);
        Assert.Contains(results, result => result.Outcome == ExchangeAccountSyncOutcome.AlreadyApplied);

        await using var verificationContext = await CreateMigratedContext();
        var persistedAccount = await new ExchangeAccountRepository(verificationContext)
            .GetByIdAsync(account.UserId, account.Id);
        var persistedPositions = await new PositionRepository(verificationContext)
            .GetByExchangeAccountAsync(account.UserId, account.Id);

        Assert.NotNull(persistedAccount);
        Assert.Equal(T1, persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(T1, persistedAccount!.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(new ConcurrencyVersion(2), persistedAccount!.Version);
        Assert.Single(persistedPositions);
        Assert.Equal(PositionTrackingState.Active, persistedPositions.Single().Value.TrackingState);
        Assert.Single(persistedPositions.Single().Value.Changes);
        Assert.Equal(
            PositionChangeKind.New,
            persistedPositions.Single().Value.Changes[0].Kind);
        Assert.Equal(
            1,
            await verificationContext.PositionChanges.CountAsync(
                change => change.PositionId == persistedPositions.Single().Value.Id.Value));
        Assert.Equal(
            1,
            await verificationContext.PortfolioStates.CountAsync(
                state => state.ExchangeAccountId == account.Id.Value));
        Assert.Equal(2, providers[0].BalanceCalls + providers[1].BalanceCalls);
        Assert.Equal(2, providers[0].PositionCalls + providers[1].PositionCalls);
    }

    [Fact]
    public async Task Newer_observation_wins_after_the_older_transaction_commits_first()
    {
        var account = CreateAccount();
        var initialPosition = Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext)
                .SaveAsync(account.UserId, account, expectedVersion: null);
            await new PositionRepository(setupContext)
                .SaveAsync(account.UserId, initialPosition, expectedVersion: null);
        }

        var olderRead = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOlderAttempt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var olderProvider = new TestPrivateProvider(CreateObservation(T1, 1m));
        var newerProvider = new TestPrivateProvider(CreateObservation(T2, 2m));

        var newerTask = RunSynchronization(
            account,
            newerProvider,
            T2,
            olderRead,
            releaseOlderAttempt);

        await olderRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
        ExchangeAccountSyncResult olderResult;
        try
        {
            olderResult = await RunSynchronization(account, olderProvider, T1);
        }
        finally
        {
            releaseOlderAttempt.TrySetResult(true);
        }

        var newerResult = await newerTask;

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, olderResult.Outcome);
        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, newerResult.Outcome);
        Assert.Equal(1, olderProvider.BalanceCalls);
        Assert.Equal(1, newerProvider.BalanceCalls);

        await using var verificationContext = await CreateMigratedContext();
        var persistedAccount = await new ExchangeAccountRepository(verificationContext)
            .GetByIdAsync(account.UserId, account.Id);
        var persistedPosition = await new PositionRepository(verificationContext)
            .GetByIdAsync(account.UserId, initialPosition.Id);
        var latestPortfolio = await new PortfolioStateRepository(verificationContext)
            .GetLatestAsync(account.UserId, account.Id);

        Assert.NotNull(persistedAccount);
        Assert.Equal(T2, persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(T2, persistedAccount!.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(new ConcurrencyVersion(3), persistedAccount!.Version);
        Assert.NotNull(persistedPosition);
        Assert.Equal(2m, persistedPosition!.Value.Size);
        Assert.Equal(T2, persistedPosition.Value.LastObservedAt);
        Assert.Equal(PositionTrackingState.Active, persistedPosition.Value.TrackingState);
        Assert.Equal(
            new[] { PositionChangeKind.New, PositionChangeKind.Increased },
            persistedPosition.Value.Changes.Select(change => change.Kind));
        Assert.NotNull(latestPortfolio);
        Assert.Equal(2_000m, latestPortfolio!.Capital.TotalEquity);
        Assert.Equal(
            2,
            await verificationContext.PortfolioStates.CountAsync(
                state => state.ExchangeAccountId == account.Id.Value));
    }

    [Fact]
    public async Task Newer_commit_first_supersedes_older_response_without_closing_position()
    {
        var account = CreateAccount();
        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext)
                .SaveAsync(account.UserId, account, expectedVersion: null);
        }

        var olderRead = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOlderAttempt = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var olderProvider = new TestPrivateProvider(CreateEmptyObservation(T1));
        var newerProvider = new TestPrivateProvider(CreateObservation(T2, 2m));

        var olderTask = RunSynchronization(
            account,
            olderProvider,
            T1,
            olderRead,
            releaseOlderAttempt);
        await olderRead.Task.WaitAsync(TimeSpan.FromSeconds(30));

        ExchangeAccountSyncResult newerResult;
        try
        {
            newerResult = await RunSynchronization(account, newerProvider, T2);
        }
        finally
        {
            releaseOlderAttempt.TrySetResult(true);
        }

        var olderResult = await olderTask;

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, newerResult.Outcome);
        Assert.Equal(ExchangeAccountSyncOutcome.Superseded, olderResult.Outcome);
        Assert.Equal(1, olderProvider.BalanceCalls);
        Assert.Equal(1, newerProvider.BalanceCalls);

        await using var verificationContext = await CreateMigratedContext();
        var persistedAccount = await new ExchangeAccountRepository(verificationContext)
            .GetByIdAsync(account.UserId, account.Id);
        var persistedPositions = await new PositionRepository(verificationContext)
            .GetByExchangeAccountAsync(account.UserId, account.Id);
        var latestPortfolio = await new PortfolioStateRepository(verificationContext)
            .GetLatestAsync(account.UserId, account.Id);

        Assert.NotNull(persistedAccount);
        Assert.Equal(T2, persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(T2, persistedAccount.Value.LastAppliedPositionsObservationAt);
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, persistedAccount.Value.ConnectionStatus);
        Assert.Null(persistedAccount.Value.LastError);
        Assert.Equal(T2.AddMinutes(1), persistedAccount.Value.LastSyncedAt);
        Assert.Single(persistedPositions);
        Assert.Equal(2m, persistedPositions.Single().Value.Size);
        Assert.Equal(T2, persistedPositions.Single().Value.LastObservedAt);
        Assert.Equal(PositionTrackingState.Active, persistedPositions.Single().Value.TrackingState);
        Assert.NotNull(latestPortfolio);
        Assert.Equal(2_000m, latestPortfolio!.Capital.TotalEquity);
        Assert.Single(
            await verificationContext.PortfolioStates
                .Where(state => state.ExchangeAccountId == account.Id.Value)
                .ToArrayAsync());
    }

    private async Task<ExchangeAccountSyncResult> RunSynchronization(
        ExchangeAccount account,
        TestPrivateProvider provider,
        DateTimeOffset calculatedAt,
        TaskCompletionSource<bool>? secondRead = null,
        TaskCompletionSource<bool>? release = null)
    {
        await using var context = fixture.CreateContext();
        IExchangeAccountRepository repository = new ExchangeAccountRepository(context);
        if (secondRead is not null && release is not null)
        {
            repository = new GateSecondAccountReadRepository(
                repository,
                secondRead,
                release);
        }

        var service = new ExchangeAccountSyncService(
            repository,
            new TestCredentialStore(),
            new TestPrivateProviderFactory(provider),
            new PositionRepository(context),
            new PortfolioStateRepository(context),
            new ExchangeAccountSyncTransaction(context),
            new FixedTimeProvider(calculatedAt.AddMinutes(1)));

        return await service.SynchronizeAsync(account.UserId, account.Id);
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static ExchangeAccount CreateAccount() =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            Capabilities);

    private static OpenPositionsObservation CreateObservation(
        DateTimeOffset observedAt,
        decimal size) =>
        OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            observedAt,
            [
                new OpenPosition(
                    "BTCUSDT",
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
                    0),
            ]);

    private static OpenPositionsObservation CreateEmptyObservation(
        DateTimeOffset observedAt) =>
        OpenPositionsObservation.Complete(
            MarketCategory.Linear,
            null,
            observedAt,
            []);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestCredentialStore : IExchangeAccountCredentialStore
    {
        private static readonly ExchangeAccountCredential Credential =
            new(
                new ExchangeAccountCredentialSecret("integration-key", "integration-secret"),
                ConcurrencyVersion.Initial);

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

    private sealed class TestPrivateProviderFactory(TestPrivateProvider provider)
        : IPrivateAccountProviderFactory
    {
        public IPrivateAccountProviderLease Create(
            ExchangeId exchange,
            ExchangeAccountCredential credentials) =>
            new TestPrivateProviderLease(provider);
    }

    private sealed class TestPrivateProviderLease(TestPrivateProvider provider)
        : IPrivateAccountProviderLease
    {
        public IPrivateAccountProvider Provider => provider;

        public void Dispose()
        {
        }
    }

    private sealed class TestPrivateProvider(
        OpenPositionsObservation positions,
        Barrier? positionsBarrier = null)
        : IPrivateAccountProvider
    {
        private readonly AccountBalanceObservation balance =
            AccountBalanceObservation.Complete(
                new AccountBalance(AccountType.Unified, positions.ObservedAt == T1 ? 1_000m : 2_000m, 900m, 800m, 1_000m, []),
                positions.ObservedAt);

        public int BalanceCalls;
        public int PositionCalls;

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref BalanceCalls);
            return Task.FromResult(balance);
        }

        public Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref PositionCalls);
            positionsBarrier?.SignalAndWait(cancellationToken);
            return Task.FromResult(positions);
        }
    }

    private sealed class GateSecondAccountReadRepository(
        IExchangeAccountRepository inner,
        TaskCompletionSource<bool> secondRead,
        TaskCompletionSource<bool> release)
        : IExchangeAccountRepository
    {
        private int readCount;

        public async Task<Versioned<ExchangeAccount>?> GetByIdAsync(
            UserId userId,
            ExchangeAccountId id,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.GetByIdAsync(userId, id, cancellationToken);
            if (Interlocked.Increment(ref readCount) == 2)
            {
                secondRead.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            }

            return result;
        }

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            ExchangeAccount account,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(userId, account, expectedVersion, cancellationToken);

        public Task DeleteAsync(
            UserId userId,
            ExchangeAccountId id,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(userId, id, expectedVersion, cancellationToken);
    }
}
