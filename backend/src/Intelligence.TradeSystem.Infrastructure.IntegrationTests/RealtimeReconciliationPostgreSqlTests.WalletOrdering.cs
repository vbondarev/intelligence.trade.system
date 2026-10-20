using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Delayed_wallet_with_exchange_clock_ahead_does_not_roll_back_newer_rest_capital_without_wallet_cursor()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var balanceStartedAt = T1.AddSeconds(20);
        var balanceCompletedAt = T1.AddSeconds(30);
        var messageCreatedAt = T1.AddSeconds(35);
        var receivedAt = T1.AddSeconds(40);
        var restClock = new MutableTimeProvider(balanceStartedAt);
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            ObservedAt = balanceCompletedAt,
            PositionsObservedAt = balanceCompletedAt,
            Balance = new AccountBalance(AccountType.Unified, 3_000m, 2_900m, 2_800m, 10m, []),
            BeforeBalanceResponse = () =>
            {
                restClock.UtcNow = balanceCompletedAt;
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition());

        await using var services = CreateApplicationServices(account, restProvider, restClock);
        await using (var restScope = services.CreateAsyncScope())
        {
            var sync = await restScope.ServiceProvider
                .GetRequiredService<ExchangeAccountSyncService>()
                .SynchronizeAsync(account.UserId, account.Id);
            Assert.True(sync.FullReconciliationCommitted);
        }

        PortfolioCapitalState capitalBefore;
        DateTimeOffset? balanceWatermarkBefore;
        DateTimeOffset? positionsWatermarkBefore;
        DateTimeOffset? lastFullyReconciledAtBefore;
        long recoveryRequestEpochBefore;
        int portfolioCountBefore;
        int outboxCountBefore;
        int portfolioUpdatedCountBefore;
        int positionChangesBefore;
        PositionId positionId;
        PositionTrackingState positionStateBefore;
        DateTimeOffset positionObservedAtBefore;

        await using (var before = fixture.CreateContext())
        {
            var portfolio = await new PortfolioStateRepository(before)
                .GetLatestAsync(account.UserId, account.Id);
            Assert.NotNull(portfolio);
            capitalBefore = portfolio!.Capital;
            Assert.Equal(3_000m, capitalBefore.TotalEquity);
            Assert.Equal(balanceCompletedAt, capitalBefore.ObservedAt);

            var persistedAccount = await new ExchangeAccountRepository(before)
                .GetByIdAsync(account.UserId, account.Id);
            Assert.NotNull(persistedAccount);
            balanceWatermarkBefore = persistedAccount!.Value.LastAppliedBalanceObservationAt;
            positionsWatermarkBefore = persistedAccount.Value.LastAppliedPositionsObservationAt;
            Assert.Equal(balanceCompletedAt, balanceWatermarkBefore);
            Assert.Equal(balanceCompletedAt, positionsWatermarkBefore);

            var state = await new RealtimeObservationStateStore(before)
                .GetAsync(account.UserId, account.Id);
            Assert.NotNull(state);
            Assert.False(state!.Value.RecoveryRequired);
            lastFullyReconciledAtBefore = state.Value.LastFullyReconciledAt;
            recoveryRequestEpochBefore = state.Value.RecoveryRequestEpoch;

            Assert.Null(await new WalletObservationCursorStore(before)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));

            var balanceEvidence = Assert.Single(await new ExchangeRestOrderingEvidenceStore(before)
                .GetBalanceEvidenceAsync(
                    account.UserId,
                    account.Id,
                    balanceStartedAt.AddSeconds(5),
                    messageCreatedAt: null));
            Assert.Equal(ExchangeRestOrderingKind.Balance, balanceEvidence.Kind);
            Assert.Equal(balanceStartedAt, balanceEvidence.StartedAt);
            Assert.Equal(balanceCompletedAt, balanceEvidence.CompletedAt);

            var position = Assert.Single(await new PositionRepository(before)
                .GetByExchangeAccountAsync(account.UserId, account.Id));
            positionId = position.Value.Id;
            positionStateBefore = position.Value.TrackingState;
            positionObservedAtBefore = position.Value.LastObservedAt;
            positionChangesBefore = await CountChangesAsync(before, positionId);
            portfolioCountBefore = await CountPortfoliosAsync(before, account);
            outboxCountBefore = await CountOutboxAsync(before, account);
            portfolioUpdatedCountBefore = await CountOutboxAsync(
                before,
                account,
                ApplicationEventTypes.PortfolioUpdated);
        }

        RealtimeObservationApplyResult result;
        await using (var walletContext = fixture.CreateContext())
        {
            result = await CreateWalletService(walletContext).ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, messageCreatedAt, totalEquity: 1_000m) with
                {
                    ReceivedAt = receivedAt,
                });
        }

        await using var after = fixture.CreateContext();
        var capitalAfter = (await new PortfolioStateRepository(after)
            .GetLatestAsync(account.UserId, account.Id))!.Capital;
        Assert.Equal(
            $"RecoveryRequired; capital={capitalBefore.TotalEquity}",
            $"{result.Outcome}; capital={capitalAfter.TotalEquity}");
        Assert.Equal(
            RealtimeObservationApplyResult.AmbiguousObservationOrder,
            result.ReasonCode);
        Assert.Equal(capitalBefore, capitalAfter);

        var accountAfter = (await new ExchangeAccountRepository(after)
            .GetByIdAsync(account.UserId, account.Id))!.Value;
        Assert.Equal(balanceWatermarkBefore, accountAfter.LastAppliedBalanceObservationAt);
        Assert.Equal(positionsWatermarkBefore, accountAfter.LastAppliedPositionsObservationAt);
        Assert.Null(await new WalletObservationCursorStore(after)
            .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));

        var stateAfter = (await new RealtimeObservationStateStore(after)
            .GetAsync(account.UserId, account.Id))!.Value;
        Assert.False(stateAfter.RecoveryRequired);
        Assert.Equal(lastFullyReconciledAtBefore, stateAfter.LastFullyReconciledAt);
        Assert.Equal(recoveryRequestEpochBefore, stateAfter.RecoveryRequestEpoch);

        var positionAfter = Assert.Single(await new PositionRepository(after)
            .GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(positionId, positionAfter.Value.Id);
        Assert.Equal(positionStateBefore, positionAfter.Value.TrackingState);
        Assert.Equal(positionObservedAtBefore, positionAfter.Value.LastObservedAt);
        Assert.Equal(positionChangesBefore, await CountChangesAsync(after, positionId));
        Assert.Equal(portfolioCountBefore, await CountPortfoliosAsync(after, account));
        Assert.Equal(outboxCountBefore, await CountOutboxAsync(after, account));
        Assert.Equal(
            portfolioUpdatedCountBefore,
            await CountOutboxAsync(after, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Wallet_cursor_persists_across_restart_and_independent_scopes()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var writer = fixture.CreateContext())
        {
            var result = await CreateWalletService(writer)
                .ApplyAsync(account.UserId, generation, WalletObservation(account, T1.AddSeconds(20)));
            Assert.Equal(RealtimeObservationApplyOutcome.Applied, result.Outcome);
        }

        await using var readerA = fixture.CreateContext();
        await using var readerB = fixture.CreateContext();
        var cursorA = await new WalletObservationCursorStore(readerA)
            .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id);
        var cursorB = await new WalletObservationCursorStore(readerB)
            .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id);
        Assert.Equal(T1.AddSeconds(20), cursorA);
        Assert.Equal(cursorA, cursorB);

        await using var olderScope = fixture.CreateContext();
        var older = await CreateWalletService(olderScope).ApplyAsync(
            account.UserId,
            generation,
            WalletObservation(account, T1.AddSeconds(10), totalEquity: 9_999m) with
            {
                ReceivedAt = T1.AddSeconds(30),
            });
        Assert.Equal(RealtimeObservationApplyOutcome.Stale, older.Outcome);

        await using var verification = fixture.CreateContext();
        var capital = await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(1_100m, capital!.Capital.TotalEquity);
        Assert.Equal(
            T1.AddSeconds(20),
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
    }

    [Fact]
    public async Task Wallet_cursor_rolls_back_with_failed_transaction()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            var inner = new ExchangeAccountSyncTransaction(context);
            var service = CreateWalletService(
                context,
                new FailingBeforeCommitTransaction(inner));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ApplyAsync(account.UserId, generation, WalletObservation(account, T1)));
        }

        await using var verification = fixture.CreateContext();
        Assert.Null(
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
        var persistedAccount = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.NotNull(persistedAccount);
        Assert.Null(persistedAccount!.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(0, await CountPortfoliosAsync(verification, account));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Unconfirmed_wallet_does_not_advance_cursor()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            var result = await CreateWalletService(context).ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1) with { Kind = PrivateWalletObservationKind.Unconfirmed, Balances = [] });
            Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, result.Outcome);
        }

        await using var verification = fixture.CreateContext();
        Assert.Null(
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
    }

    [Fact]
    public async Task Wallet_cursor_is_isolated_by_account_ownership()
    {
        var (accountA, generationA) = await CreateObservedAccountAsync();
        var (accountB, generationB) = await CreateObservedAccountAsync();

        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(
                RealtimeObservationApplyOutcome.Applied,
                (await CreateWalletService(context).ApplyAsync(
                    accountA.UserId,
                    generationA,
                    WalletObservation(accountA, T1.AddSeconds(20)))).Outcome);
        }

        await using var foreign = fixture.CreateContext();
        Assert.Null(
            await new WalletObservationCursorStore(foreign)
                .GetLastAcceptedMessageCreatedAtAsync(accountB.UserId, accountA.Id));
        Assert.Null(
            await new WalletObservationCursorStore(foreign)
                .GetLastAcceptedMessageCreatedAtAsync(accountA.UserId, accountB.Id));

        await using (var context = fixture.CreateContext())
        {
            Assert.Equal(
                RealtimeObservationApplyOutcome.Applied,
                (await CreateWalletService(context).ApplyAsync(
                    accountB.UserId,
                    generationB,
                    WalletObservation(accountB, T1.AddSeconds(5)))).Outcome);
        }

        await using var verification = fixture.CreateContext();
        Assert.Equal(
            T1.AddSeconds(20),
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(accountA.UserId, accountA.Id));
        Assert.Equal(
            T1.AddSeconds(5),
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(accountB.UserId, accountB.Id));
    }

    [Fact]
    public async Task Rest_balance_respects_persisted_wallet_cursor_across_scopes()
    {
        var (account, generation) = await CreateObservedAccountAsync();

        await using (var walletContext = fixture.CreateContext())
        {
            var accepted = WalletObservation(account, T1.AddSeconds(30), totalEquity: 3_000m) with
            {
                ReceivedAt = T1.AddSeconds(10),
            };
            Assert.Equal(
                RealtimeObservationApplyOutcome.Applied,
                (await CreateWalletService(walletContext)
                    .ApplyAsync(account.UserId, generation, accepted)).Outcome);
        }

        var clock = new MutableTimeProvider(T1.AddSeconds(15));
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            ObservedAt = T1.AddSeconds(15),
            PositionsObservedAt = T1.AddSeconds(15),
            Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
            BeforeBalanceResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition());

        await using var services = CreateApplicationServices(account, provider, clock);
        await using var scope = services.CreateAsyncScope();
        var sync = await scope.ServiceProvider
            .GetRequiredService<ExchangeAccountSyncService>()
            .SynchronizeAsync(account.UserId, account.Id);

        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, sync.Outcome);
        await using var verification = fixture.CreateContext();
        var capital = await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(3_000m, capital!.Capital.TotalEquity);
        Assert.Equal(
            T1.AddSeconds(30),
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
        var accountState = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.Equal("balance_ambiguous", accountState!.Value.LastError);
    }

    /// <summary>
    /// P1/P2: REST, начатый до wallet commit в независимом DI scope, не откатывает капитал,
    /// когда биржевой <c>MessageCreatedAt</c> отстаёт от backend <c>RequestStartedAt</c>.
    /// </summary>
    [Fact]
    public async Task Rest_Does_Not_Overwrite_Wallet_Committed_During_Read_When_Exchange_Clock_Lags()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var t15 = T1.AddSeconds(15);
        var t20 = T1.AddSeconds(20);
        var t25 = T1.AddSeconds(25);
        var t30 = T1.AddSeconds(30);

        var restCapturedStaleBalance = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var walletCommitted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var restClock = new MutableTimeProvider(t20);
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = t30,
            Balance = new AccountBalance(AccountType.Unified, 1_000m, 900m, 800m, 10m, []),
        };
        restProvider.SetPosition(OpenPosition());
        restProvider.BeforeBalanceResponse = async () =>
        {
            restProvider.BeforeBalanceResponse = null;
            // RequestStartedAt уже зафиксирован как T20; устаревший баланс захвачен провайдером.
            Assert.True(restCapturedStaleBalance.TrySetResult(true));
            Assert.True(
                await walletCommitted.Task.WaitAsync(CompletionTimeout),
                "Wallet commit must finish before REST resumes.");
            restClock.UtcNow = t30;
        };
        restProvider.BeforePositionsResponse = () =>
        {
            restClock.UtcNow = t30;
            return Task.CompletedTask;
        };

        // Один root provider: Scope A (wallet) и Scope B (REST) с независимыми scoped DbContext.
        await using var services = CreateApplicationServices(account, restProvider, restClock);
        await using var scopeA = services.CreateAsyncScope();
        await using var scopeB = services.CreateAsyncScope();

        var dbContextA = scopeA.ServiceProvider.GetRequiredService<TradeSystemDbContext>();
        var dbContextB = scopeB.ServiceProvider.GetRequiredService<TradeSystemDbContext>();
        Assert.NotSame(dbContextA, dbContextB);

        int portfolioUpdatedBefore;
        int syncDegradedBefore;
        int accountUpdatedBefore;
        int positionOpenedBefore;
        await using (var baseline = fixture.CreateContext())
        {
            portfolioUpdatedBefore = await CountOutboxAsync(
                baseline, account, ApplicationEventTypes.PortfolioUpdated);
            syncDegradedBefore = await CountOutboxAsync(
                baseline, account, ApplicationEventTypes.ExchangeAccountSyncDegraded);
            accountUpdatedBefore = await CountOutboxAsync(
                baseline, account, ApplicationEventTypes.ExchangeAccountUpdated);
            positionOpenedBefore = await CountOutboxAsync(
                baseline, account, ApplicationEventTypes.PositionOpened);
        }

        var walletService = scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>();
        var restService = scopeB.ServiceProvider
            .GetRequiredService<ExchangeAccountSyncService>();

        // Scope B: REST balance request начинается до wallet и удерживается до commit Scope A.
        var restSync = restService.SynchronizeAsync(account.UserId, account.Id);

        Assert.True(
            await restCapturedStaleBalance.Task.WaitAsync(CompletionTimeout),
            "REST must capture the stale balance before wallet runs.");

        try
        {
            // Scope A: wallet WS коммитит капитал 3000 при отстающих часах биржи (MessageCreatedAt=T15).
            var wallet = WalletObservation(account, t15, totalEquity: 3_000m) with
            {
                ReceivedAt = t25,
            };
            Assert.Equal(
                RealtimeObservationApplyOutcome.Applied,
                (await walletService.ApplyAsync(account.UserId, generation, wallet)).Outcome);

            await using (var midVerification = fixture.CreateContext())
            {
                var midCapital = await new PortfolioStateRepository(midVerification)
                    .GetLatestAsync(account.UserId, account.Id);
                Assert.Equal(3_000m, midCapital!.Capital.TotalEquity);
                Assert.Equal(t15, midCapital.Capital.ObservedAt);
                Assert.Equal(
                    t15,
                    await new WalletObservationCursorStore(midVerification)
                        .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));
            }
        }
        finally
        {
            // Разблокируем REST даже при сбое wallet-ветки, чтобы исходный Assert не маскировался таймаутом.
            walletCommitted.TrySetResult(true);
        }

        var syncResult = await restSync.WaitAsync(CompletionTimeout);
        Assert.Equal(ExchangeAccountSyncOutcome.ExchangeUnavailable, syncResult.Outcome);
        Assert.False(syncResult.FullReconciliationCommitted);
        Assert.Null(syncResult.BalanceReadStartedAt);
        Assert.Null(syncResult.BalanceReadCompletedAt);

        // Scope C / независимый DbContext: окончательное состояние общей PostgreSQL.
        await using var verification = fixture.CreateContext();
        var capital = await new PortfolioStateRepository(verification)
            .GetLatestAsync(account.UserId, account.Id);
        Assert.Equal(3_000m, capital!.Capital.TotalEquity);
        Assert.Equal(t15, capital.Capital.ObservedAt);
        Assert.Equal(
            t15,
            await new WalletObservationCursorStore(verification)
                .GetLastAcceptedMessageCreatedAtAsync(account.UserId, account.Id));

        var accountState = await new ExchangeAccountRepository(verification)
            .GetByIdAsync(account.UserId, account.Id);
        Assert.Equal("balance_ambiguous", accountState!.Value.LastError);
        Assert.Equal(t15, accountState.Value.LastAppliedBalanceObservationAt);
        Assert.Null(accountState.Value.LastSyncedAt);

        // Отвергнутый REST balance не создаёт Balance evidence и не делает капитал 1000 подтверждённым.
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));
        Assert.NotEqual(1_000m, capital.Capital.TotalEquity);

        Assert.Equal(
            portfolioUpdatedBefore + 2,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
        Assert.Equal(
            syncDegradedBefore + 1,
            await CountOutboxAsync(
                verification, account, ApplicationEventTypes.ExchangeAccountSyncDegraded));
        Assert.Equal(
            accountUpdatedBefore,
            await CountOutboxAsync(
                verification, account, ApplicationEventTypes.ExchangeAccountUpdated));
        // Принятые позиции REST допустимы; ложного полного успешного sync-события быть не должно.
        Assert.Equal(
            positionOpenedBefore + 1,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionOpened));
    }
}
