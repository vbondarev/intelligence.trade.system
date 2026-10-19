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
public sealed partial class RealtimeReconciliationPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset T1 = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan BlockedCommandProbe = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);
    private static readonly SettlementAsset Usdt = SettlementAsset.From("USDT");
    private static readonly SettlementAsset Usdc = SettlementAsset.From("USDC");

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

    [Fact]
    public async Task Position_update_after_a_clock_rollback_remains_the_latest_portfolio_snapshot()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1, 1));
        var persistedAt = T1.AddMinutes(10);
        await SeedNewerPortfolioTimestampAsync(account, persistedAt);

        var result = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(1), 2, OpenPosition(size: 2m)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        await using var verification = fixture.CreateContext();
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latest);
        Assert.Equal(persistedAt.AddTicks(TimeSpan.TicksPerMicrosecond), latest.CalculatedAt);
        Assert.Equal(2m, Assert.Single(latest.Positions).Size);
    }

    [Fact]
    public async Task Wallet_update_after_a_clock_rollback_remains_the_latest_portfolio_snapshot()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await using (var context = fixture.CreateContext())
        {
            await CreateWalletService(context).ApplyAsync(account.UserId, generation, WalletObservation(account, T1));
        }

        var persistedAt = T1.AddMinutes(10);
        await SeedNewerPortfolioTimestampAsync(account, persistedAt);
        await using (var context = fixture.CreateContext())
        {
            var result = await CreateWalletService(context)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1.AddSeconds(1)));
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        }

        await using var verification = fixture.CreateContext();
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latest);
        Assert.Equal(persistedAt.AddTicks(TimeSpan.TicksPerMicrosecond), latest.CalculatedAt);
        Assert.Equal(1_100m, latest.Capital.TotalEquity);
        Assert.Equal(T1.AddSeconds(1), latest.Capital.ObservedAt);
    }

    [Fact]
    public async Task An_equal_portfolio_timestamp_is_advanced_by_one_postgresql_microsecond()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1, 1));
        var persistedAt = T1.AddMinutes(10);
        await SeedNewerPortfolioTimestampAsync(account, persistedAt);

        var result = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, persistedAt, 2, OpenPosition(size: 2m)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        await using var verification = fixture.CreateContext();
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latest);
        Assert.Equal(persistedAt.AddTicks(TimeSpan.TicksPerMicrosecond), latest.CalculatedAt);
        Assert.Equal(2m, Assert.Single(latest.Positions).Size);
    }

    [Fact]
    public async Task Fast_position_wallet_position_updates_keep_the_latest_read_model_monotonic()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        await ApplyAsync(account, generation, OpenObservation(account, T1, 1));
        var persistedAt = T1.AddMinutes(10);
        await SeedNewerPortfolioTimestampAsync(account, persistedAt);

        await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(1), 2, OpenPosition(size: 2m)));
        await using (var walletContext = fixture.CreateContext())
        {
            await CreateWalletService(walletContext)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1.AddSeconds(2)));
        }

        await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(3), 3, OpenPosition(size: 3m)));

        await using var verification = fixture.CreateContext();
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(latest);
        Assert.Equal(persistedAt.AddTicks(3 * TimeSpan.TicksPerMicrosecond), latest.CalculatedAt);
        Assert.Equal(1_100m, latest.Capital.TotalEquity);
        Assert.Equal(T1.AddSeconds(2), latest.Capital.ObservedAt);
        Assert.Equal(3m, Assert.Single(latest.Positions).Size);
        Assert.Equal(T1.AddSeconds(3), latest.Positions.Single().LastObservedAt);
    }

    [Fact]
    public async Task Delayed_open_older_than_a_failed_rest_attempt_is_confirmed_and_becomes_the_latest_snapshot()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var failedAttemptAt = T1.AddSeconds(10);
        await using (var services = CreateApplicationServices(
                         account,
                         new TestPrivateProvider
                         {
                             ObservedAt = failedAttemptAt,
                             Status = OpenPositionsObservationStatus.Failed,
                         }))
        {
            var sync = await services.GetRequiredService<ExchangeAccountObservationCoordinator>()
                .SynchronizeAsync(account.UserId, account.Id);
            Assert.False(sync.FullReconciliationCommitted);
        }

        var confirmation = new TestPrivateProvider { ObservedAt = T1.AddSeconds(11) };
        confirmation.SetPosition(OpenPosition());

        var result = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddSeconds(9), 1),
            confirmation);

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        Assert.True(result.ConfirmedByRest);
        await using var verification = fixture.CreateContext();
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(failedAttemptAt, stored!.Value.LastAppliedPositionsObservationAt);
        var position = Assert.Single(await new PositionRepository(verification)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(PositionTrackingState.Active, position.Value.TrackingState);
        var cursor = await new PositionObservationCursorStore(verification).GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(1, cursor!.Value.LastProviderSequence);
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1m, Assert.Single(latest!.Positions).Size);
        Assert.Equal(
            latest.CalculatedAt,
            await verification.PortfolioStates
                .Where(state => state.ExchangeAccountId == account.Id.Value)
                .MaxAsync(state => state.CalculatedAt));
        Assert.Equal(1, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
    }

    [Fact]
    public async Task Sub_microsecond_observations_keep_snapshot_order_and_equal_time_wallet_semantics()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var openedAt = T1.AddMinutes(5).AddTicks(1);
        var walletAt = T1.AddMinutes(5).AddTicks(3);

        var opened = await ApplyAsync(account, generation, OpenObservation(account, openedAt, 1));
        RealtimeObservationApplyResult wallet;
        RealtimeObservationApplyResult repeated;
        RealtimeObservationApplyResult conflicting;
        await using (var context = fixture.CreateContext())
        {
            wallet = await CreateWalletService(context)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, walletAt));
        }

        await using (var context = fixture.CreateContext())
        {
            repeated = await CreateWalletService(context)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, walletAt.AddTicks(4)));
        }

        await using (var context = fixture.CreateContext())
        {
            conflicting = await CreateWalletService(context).ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, walletAt.AddTicks(6), totalEquity: 1_200m));
        }

        var updated = await ApplyAsync(
            account,
            generation,
            OpenObservation(account, T1.AddMinutes(6).AddTicks(1), 2, OpenPosition(size: 2m)));

        Assert.Equal(RealtimeObservationApplyOutcome.Applied, opened.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Duplicate, repeated.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, conflicting.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.BalanceConflict, conflicting.ReasonCode);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, updated.Outcome);
        await using var verification = fixture.CreateContext();
        var snapshots = await verification.PortfolioStates
            .Where(state => state.ExchangeAccountId == account.Id.Value)
            .OrderBy(state => state.CalculatedAt)
            .Select(state => state.CalculatedAt)
            .ToArrayAsync();
        Assert.Equal(
            new[]
            {
                T1.AddMinutes(5).AddTicks(TimeSpan.TicksPerMicrosecond),
                T1.AddMinutes(5).AddTicks(2 * TimeSpan.TicksPerMicrosecond),
                T1.AddMinutes(6).AddTicks(TimeSpan.TicksPerMicrosecond),
            },
            snapshots);
        var latest = await new PortfolioStateRepository(verification).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddMinutes(6).AddTicks(TimeSpan.TicksPerMicrosecond), latest!.CalculatedAt);
        Assert.Equal(1_100m, latest.Capital.TotalEquity);
        Assert.Equal(2m, Assert.Single(latest.Positions).Size);
        Assert.Equal(3, await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Stale_rest_confirmation_keeps_position_cursor_portfolio_and_outbox_unchanged()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 10))).Outcome);
        int portfoliosBefore;
        int outboxBefore;
        await using (var context = fixture.CreateContext())
        {
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
        }

        var result = await ApplyAsync(
            account,
            generation,
            CloseObservation(account, T1.AddSeconds(10), 20),
            new TestPrivateProvider { ObservedAt = T1.AddSeconds(8) });

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.RestConfirmationStale, result.ReasonCode);
        await using var verification = fixture.CreateContext();
        await AssertOpenPositionUnchangedAsync(verification, account, portfoliosBefore, outboxBefore);
    }

    [Fact]
    public async Task Rest_confirmed_close_failure_before_commit_rolls_back_position_cursor_portfolio_and_outbox()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        Assert.Equal(
            RealtimeObservationApplyOutcome.Applied,
            (await ApplyAsync(account, generation, OpenObservation(account, T1, 10))).Outcome);
        int portfoliosBefore;
        int outboxBefore;
        await using (var context = fixture.CreateContext())
        {
            portfoliosBefore = await CountPortfoliosAsync(context, account);
            outboxBefore = await CountOutboxAsync(context, account);
        }

        await using (var context = fixture.CreateContext())
        {
            var service = CreateRealtimeService(
                context,
                new TestPrivateProvider { ObservedAt = T1.AddSeconds(12) },
                new FailingBeforeCommitTransaction(new ExchangeAccountSyncTransaction(context), failingExecution: 2));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplyAsync(account.UserId, generation, CloseObservation(account, T1.AddSeconds(10), 20)));
        }

        await using var verification = fixture.CreateContext();
        await AssertOpenPositionUnchangedAsync(verification, account, portfoliosBefore, outboxBefore);
    }

    [Fact]
    public async Task Wallet_observation_newer_than_rest_balance_survives_overlapping_full_sync()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var walletAt = T1.AddSeconds(2);
        ExchangeAccountObservationCoordinator? coordinator = null;
        var restProvider = new TestPrivateProvider
        {
            BeforePositionsResponse = () =>
            {
                Assert.True(coordinator!.TryEnqueue(account.UserId, generation, WalletObservation(account, walletAt)));
                return Task.CompletedTask;
            },
        };
        await using var services = CreateApplicationServices(account, restProvider);
        coordinator = services.GetRequiredService<ExchangeAccountObservationCoordinator>();

        var sync = await coordinator.SynchronizeAsync(account.UserId, account.Id);
        var applied = await coordinator.ProcessPendingAsync(account.UserId, account.Id);
        Assert.True(coordinator.TryEnqueue(account.UserId, generation, WalletObservation(account, walletAt)));
        var repeated = await coordinator.ProcessPendingAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, sync.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, Assert.Single(applied).Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Duplicate, Assert.Single(repeated).Outcome);
        await using var context = fixture.CreateContext();
        await AssertWalletCapitalAsync(context, account, walletAt, positionsWatermark: T1);
        Assert.Equal(2, await CountPortfoliosAsync(context, account));
        Assert.Equal(2, await CountOutboxAsync(context, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Wallet_observation_racing_with_rest_sync_is_not_overwritten_by_the_older_rest_balance()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var walletAt = T1.AddSeconds(2);
        using var barrier = new Barrier(2);
        await using var services = CreateApplicationServices(account, new TestPrivateProvider(barrier));

        var sync = Task.Run(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<IExchangeAccountSyncService>()
                .SynchronizeAsync(account.UserId, account.Id);
        });
        var wallet = Task.Run(async () =>
        {
            barrier.SignalAndWait(CompletionTimeout);
            await using var context = fixture.CreateContext();
            return await CreateWalletService(context).ApplyAsync(account.UserId, generation, WalletObservation(account, walletAt));
        });
        var syncResult = await sync.WaitAsync(CompletionTimeout);
        var walletResult = await wallet.WaitAsync(CompletionTimeout);
        RealtimeObservationApplyResult repeated;
        await using (var context = fixture.CreateContext())
        {
            repeated = await CreateWalletService(context)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, walletAt));
        }

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, syncResult.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Applied, walletResult.Outcome);
        Assert.Equal(RealtimeObservationApplyOutcome.Duplicate, repeated.Outcome);
        await using var verification = fixture.CreateContext();
        await AssertWalletCapitalAsync(verification, account, walletAt, positionsWatermark: T1);
        Assert.Equal(2, await CountPortfoliosAsync(verification, account));
        Assert.Equal(2, await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Wallet_failure_before_commit_rolls_back_watermark_portfolio_and_outbox()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            var service = CreateWalletService(
                context,
                new FailingBeforeCommitTransaction(new ExchangeAccountSyncTransaction(context)));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplyAsync(account.UserId, generation, WalletObservation(account, T1)));
        }

        await using var verification = fixture.CreateContext();
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Null(stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Generation_change_committed_while_the_wallet_observation_waits_rejects_it_without_writes()
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

            apply = CreateWalletService(observerContext)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1));
            Assert.NotSame(apply, await Task.WhenAny(apply, Task.Delay(BlockedCommandProbe)));

            await transaction.CommitAsync();
        }

        var result = await apply.WaitAsync(CompletionTimeout);
        Assert.Equal(RealtimeObservationApplyOutcome.GenerationSuperseded, result.Outcome);
        await using var verification = fixture.CreateContext();
        var stored = await new ExchangeAccountRepository(verification).GetByIdAsync(account.UserId, account.Id);
        Assert.Null(stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    private static async Task AssertOpenPositionUnchangedAsync(
        TradeSystemDbContext context,
        ExchangeAccount account,
        int portfoliosBefore,
        int outboxBefore)
    {
        var position = Assert.Single(await new PositionRepository(context)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(PositionTrackingState.Active, position.Value.TrackingState);
        Assert.Equal(T1, position.Value.LastObservedAt);
        Assert.Equal(1, await CountChangesAsync(context, position.Value.Id));
        var cursor = await new PositionObservationCursorStore(context).GetAsync(account.UserId, CursorKey(account));
        Assert.Equal(10, cursor!.Value.LastProviderSequence);
        Assert.Equal(portfoliosBefore, await CountPortfoliosAsync(context, account));
        Assert.Equal(outboxBefore, await CountOutboxAsync(context, account));
        Assert.Equal(0, await CountOutboxAsync(context, account, ApplicationEventTypes.PositionClosed));
    }

    private static async Task AssertWalletCapitalAsync(
        TradeSystemDbContext context,
        ExchangeAccount account,
        DateTimeOffset walletAt,
        DateTimeOffset positionsWatermark)
    {
        var stored = await new ExchangeAccountRepository(context).GetByIdAsync(account.UserId, account.Id);
        Assert.Equal(walletAt, stored!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(positionsWatermark, stored.Value.LastAppliedPositionsObservationAt);
        var portfolio = await new PortfolioStateRepository(context).GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1_100m, portfolio!.Capital.TotalEquity);
        Assert.Equal(walletAt, portfolio.Capital.ObservedAt);
    }

    private async Task SeedNewerPortfolioTimestampAsync(ExchangeAccount account, DateTimeOffset calculatedAt)
    {
        await using var context = fixture.CreateContext();
        var positions = (await new PositionRepository(context)
                .GetByExchangeAccountAsync(account.UserId, account.Id))
            .Select(versioned => versioned.Value)
            .ToArray();
        var previous = await new PortfolioStateRepository(context).GetLatestAsync(account.UserId, account.Id);
        Assert.NotNull(previous);
        var state = PortfolioState.Create(
            account.Id,
            positions,
            previous.Capital,
            calculatedAt,
            TimeSpan.FromMinutes(5),
            previous.PositionsFullyReconciled);
        await new PortfolioStateRepository(context).SaveAsync(account.UserId, state);
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
        TestPrivateProvider? provider = null,
        TimeProvider? clock = null,
        bool confirmWithRest = false)
    {
        await using var context = fixture.CreateContext();
        return await CreateRealtimeService(context, provider ?? new TestPrivateProvider(), clock: clock)
            .ApplyAsync(account.UserId, generation, observation, confirmWithRest);
    }

    /// <summary>
    /// Production-регистрация Application поверх PostgreSQL-репозиториев: REST-синхронизации проходят
    /// через тот же coordinator, что и в API и фоновой синхронизации.
    /// </summary>
    /// <param name="decorateStateStore">
    /// Тестовый декоратор поверх <see cref="RealtimeObservationStateStore"/> без production hooks.
    /// </param>
    private ServiceProvider CreateApplicationServices(
        ExchangeAccount account,
        TestPrivateProvider provider,
        TimeProvider? clock = null,
        Func<IRealtimeObservationStateStore, IRealtimeObservationStateStore>? decorateStateStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock ?? new FixedTimeProvider(T1.AddMinutes(1)));
        services.AddScoped(_ => fixture.CreateContext());
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IPortfolioStateRepository, PortfolioStateRepository>();
        services.AddScoped<IPositionObservationCursorStore, PositionObservationCursorStore>();
        services.AddScoped<IRealtimeObservationStateStore>(serviceProvider =>
        {
            IRealtimeObservationStateStore store = new RealtimeObservationStateStore(
                serviceProvider.GetRequiredService<TradeSystemDbContext>());
            return decorateStateStore?.Invoke(store) ?? store;
        });
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
        IExchangeAccountSyncTransaction? transaction = null,
        TimeProvider? clock = null) =>
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
            clock ?? new FixedTimeProvider(T1.AddMinutes(1)));

    private static RealtimeWalletObservationService CreateWalletService(
        TradeSystemDbContext context,
        IExchangeAccountSyncTransaction? transaction = null) =>
        new(
            new ExchangeAccountRepository(context),
            new PositionRepository(context),
            new PortfolioStateRepository(context),
            new RealtimeObservationStateStore(context),
            transaction ?? new ExchangeAccountSyncTransaction(context),
            new ApplicationEventOutbox(context),
            new FixedTimeProvider(T1.AddMinutes(1)));

    private static PrivateWalletObservation WalletObservation(
        ExchangeAccount account,
        DateTimeOffset at,
        decimal totalEquity = 1_100m) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = account.Id,
            Kind = PrivateWalletObservationKind.PartialUpdate,
            Balances = [new AccountBalance(AccountType.Unified, totalEquity, 1_000m, 900m, 20m, [])],
            MessageCreatedAt = at,
            ReceivedAt = at.AddMilliseconds(50),
        };

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

    private static OpenPosition OpenPosition(
        string symbol = "BTCUSDT",
        decimal size = 1m,
        PositionSide side = PositionSide.Long,
        int positionIdx = 0,
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
            positionIdx,
            settlementAsset ?? Usdt);

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

    private static PrivatePositionObservation CloseObservation(
        ExchangeAccount account,
        DateTimeOffset at,
        long sequence,
        string symbol = "BTCUSDT") =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            ExchangeAccountId = account.Id,
            Kind = PrivatePositionObservationKind.PotentialClose,
            Symbol = symbol,
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

    /// <summary>
    /// Завершает исключением транзакцию с порядковым номером <paramref name="failingExecution"/> после
    /// всех её записей, но до commit; предыдущие транзакции фиксируются.
    /// </summary>
    private sealed class FailingBeforeCommitTransaction(IExchangeAccountSyncTransaction inner, int failingExecution = 1)
        : IExchangeAccountSyncTransaction
    {
        private int executions;

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
            ++executions != failingExecution
                ? inner.ExecuteAsync(operation, cancellationToken)
                : inner.ExecuteAsync(
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

        public DateTimeOffset ObservedAt { get; set; } = T1;

        /// <summary>
        /// Момент REST-снимка позиций независимо от часов backend и <see cref="ObservedAt"/> баланса.
        /// </summary>
        public DateTimeOffset? PositionsObservedAt { get; set; }

        /// <summary>Часы для интервала чтения баланса; без них <see cref="ObservedAt"/> задаёт обе границы.</summary>
        public MutableTimeProvider? Clock { get; set; }

        public AccountBalance Balance { get; set; } =
            new(AccountType.Unified, 1_000m, 900m, 800m, 10m, []);

        public OpenPositionsObservationStatus Status { get; set; } = OpenPositionsObservationStatus.Complete;

        /// <summary>REST-запрос баланса завершается ошибкой биржи.</summary>
        public bool BalanceFails { get; set; }

        /// <summary>Выполняется во время REST-запроса позиций: после снимка баланса, до ответа.</summary>
        public Func<Task>? BeforePositionsResponse { get; set; }

        /// <summary>Выполняется во время REST-запроса баланса, после фиксации начала чтения.</summary>
        public Func<Task>? BeforeBalanceResponse { get; set; }

        /// <summary>Символы scoped/full REST-запросов позиций в порядке вызовов.</summary>
        public List<string?> PositionRequests { get; } = [];

        public void SetPosition(OpenPosition position) => positions.Add(position);

        public Task<ApiKeyAccessMetadataObservation> GetApiKeyAccessMetadataAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<AccountBalanceObservation> GetWalletBalanceAsync(
            AccountType accountType,
            CancellationToken cancellationToken = default)
        {
            var requestStartedAt = Clock?.GetUtcNow() ?? ObservedAt;
            if (BeforeBalanceResponse is not null)
            {
                await BeforeBalanceResponse();
            }

            var observedAt = Clock?.GetUtcNow() ?? ObservedAt;
            return BalanceFails
                ? AccountBalanceObservation.Failed(
                    new ExchangeFailure(ExchangeFailureKind.Unknown, Retryable: false),
                    observedAt,
                    requestStartedAt)
                : AccountBalanceObservation.Complete(Balance, observedAt, requestStartedAt);
        }

        public async Task<OpenPositionsObservation> GetOpenPositionsAsync(
            MarketCategory category,
            string? symbol = null,
            CancellationToken cancellationToken = default)
        {
            PositionRequests.Add(symbol);
            positionsBarrier?.SignalAndWait(CompletionTimeout, cancellationToken);
            if (BeforePositionsResponse is not null)
            {
                await BeforePositionsResponse();
            }

            var observedAt = PositionsObservedAt ?? Clock?.GetUtcNow() ?? ObservedAt;
            var observed = positions.Where(position => symbol is null || position.Symbol == symbol).ToArray();
            return Status switch
            {
                OpenPositionsObservationStatus.Complete =>
                    OpenPositionsObservation.Complete(category, symbol, observedAt, observed),
                OpenPositionsObservationStatus.Partial =>
                    OpenPositionsObservation.Partial(category, symbol, observedAt, observed, "partial"),
                _ => OpenPositionsObservation.Failed(category, symbol, observedAt, "failed"),
            };
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
