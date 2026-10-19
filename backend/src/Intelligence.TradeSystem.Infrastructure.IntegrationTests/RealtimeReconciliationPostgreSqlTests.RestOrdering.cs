using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
    [Fact]
    public async Task Rest_positions_on_instance_b_blocks_delayed_ws_size_rollback_on_instance_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition(size: 2m));
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var delayed = await scopeA.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 1m)));

        Assert.True(
            delayed.Outcome is RealtimeObservationApplyOutcome.Applied or RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(delayed.ConfirmedByRest);
        }

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(2m, position.Size);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
    }

    [Fact]
    public async Task Rest_balance_then_wallet_inside_interval_on_other_instance_requires_recovery()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforeBalanceResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition());
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.Equal(T1.AddSeconds(10), sync.BalanceReadStartedAt);
        Assert.Equal(T1.AddSeconds(20), sync.BalanceReadCompletedAt);
        var equityBefore = (await LatestPortfolioAsync(account)).Capital.TotalEquity;

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var wallet = await scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(15), totalEquity: 9_999m));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, wallet.ReasonCode);
        Assert.Equal(equityBefore, (await LatestPortfolioAsync(account)).Capital.TotalEquity);
    }

    [Fact]
    public async Task Partial_rest_persists_scoped_symbol_without_full_positions_complete()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1.AddSeconds(10));
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1.AddSeconds(10),
            Status = OpenPositionsObservationStatus.Partial,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(15);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition("BTCUSDT"));
        var result = await SynchronizeViaScopeAsync(account, provider, clock);

        Assert.False(result.FullPositionsCompleteAccepted);
        Assert.True(result.AcceptedPositionsObservation);
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.ScopedSymbol, "BTCUSDT"));
    }

    [Fact]
    public async Task Rest_evidence_persists_without_realtime_observation_state()
    {
        var account = await CreateAccountWithoutObservationAsync();
        await using (var context = fixture.CreateContext())
        {
            Assert.Null(await new RealtimeObservationStateStore(context).GetAsync(account.UserId, account.Id));
        }

        var clock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(2);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition());
        var result = await SynchronizeViaScopeAsync(account, provider, clock);

        Assert.Equal(ExchangeAccountSyncOutcome.Synchronized, result.Outcome);
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.True(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));
        await using var verification = fixture.CreateContext();
        Assert.Null(await new RealtimeObservationStateStore(verification).GetAsync(account.UserId, account.Id));
    }

    [Fact]
    public async Task Sync_rollback_does_not_leave_rest_ordering_evidence()
    {
        var (account, _) = await CreateObservedAccountAsync();
        var clock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = clock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                clock.UtcNow = T1.AddSeconds(1);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 2m));

        await using var context = fixture.CreateContext();
        var inner = new ExchangeAccountSyncTransaction(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExchangeAccountSyncService(
                    new ExchangeAccountRepository(context),
                    new TestCredentialStore(),
                    new TestPrivateProviderFactory(provider),
                    new PositionRepository(context),
                    new PortfolioStateRepository(context),
                    new FailingBeforeCommitTransaction(inner),
                    new ApplicationEventOutbox(context),
                    new ExchangeRestOrderingEvidenceStore(context),
                    clock)
                .SynchronizeAsync(account.UserId, account.Id));

        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.FullPositionsComplete));
        Assert.False(await HasRestOrderingKindAsync(account, ExchangeRestOrderingKind.Balance));
        await using var verification = fixture.CreateContext();
        Assert.Empty(await new PositionRepository(verification).GetByExchangeAccountAsync(account.UserId, account.Id));
        Assert.Equal(0, await CountOutboxAsync(verification, account));
    }

    [Fact]
    public async Task Missing_evidence_after_rest_on_b_does_not_allow_delayed_ws_rollback_on_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforePositionsResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition(size: 3m));
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.True(sync.FullPositionsCompleteAccepted);
        await ClearRestOrderingEvidenceAsync(account);

        restProvider.PositionsObservedAt = T1.AddMinutes(2);
        restProvider.Clock = new MutableTimeProvider(T1.AddMinutes(2));
        await using var instanceA = CreateApplicationServices(
            account,
            restProvider,
            new FixedTimeProvider(T1.AddMinutes(2)));
        await using var scopeA = instanceA.CreateAsyncScope();
        var delayed = await scopeA.ServiceProvider
            .GetRequiredService<RealtimePositionReconciliationService>()
            .ApplyAsync(account.UserId, generation, OpenObservation(account, T1.AddSeconds(15), 1, OpenPosition(size: 2m)));

        Assert.True(
            delayed.Outcome is RealtimeObservationApplyOutcome.Applied
                or RealtimeObservationApplyOutcome.RecoveryRequired);
        if (delayed.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(delayed.ConfirmedByRest);
        }

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.Equal(3m, position.Size);
        Assert.Equal(0, await CountOutboxAsync(verification, account, ApplicationEventTypes.PositionChanged));
    }

    [Fact]
    public async Task Missing_balance_evidence_after_rest_on_b_requires_recovery_for_ambiguous_wallet_on_a()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var restClock = new MutableTimeProvider(T1.AddSeconds(10));
        var restProvider = new TestPrivateProvider
        {
            Clock = restClock,
            PositionsObservedAt = T1.AddSeconds(10),
            BeforeBalanceResponse = () =>
            {
                restClock.UtcNow = T1.AddSeconds(20);
                return Task.CompletedTask;
            },
        };
        restProvider.SetPosition(OpenPosition());
        var sync = await SynchronizeViaScopeAsync(account, restProvider, restClock);
        Assert.Equal(T1.AddSeconds(10), sync.BalanceReadStartedAt);
        Assert.Equal(T1.AddSeconds(20), sync.BalanceReadCompletedAt);
        var equityBefore = (await LatestPortfolioAsync(account)).Capital.TotalEquity;
        await ClearRestOrderingEvidenceAsync(account);
        int outboxBefore;
        await using (var beforeContext = fixture.CreateContext())
        {
            outboxBefore = await CountOutboxAsync(beforeContext, account, ApplicationEventTypes.PortfolioUpdated);
        }

        await using var instanceA = CreateApplicationServices(account, restProvider);
        await using var scopeA = instanceA.CreateAsyncScope();
        var wallet = await scopeA.ServiceProvider
            .GetRequiredService<RealtimeWalletObservationService>()
            .ApplyAsync(
                account.UserId,
                generation,
                WalletObservation(account, T1.AddSeconds(15), totalEquity: 9_999m));

        Assert.Equal(RealtimeObservationApplyOutcome.RecoveryRequired, wallet.Outcome);
        Assert.Equal(RealtimeObservationApplyResult.AmbiguousWithRestBalance, wallet.ReasonCode);
        Assert.Equal(equityBefore, (await LatestPortfolioAsync(account)).Capital.TotalEquity);
        await using var verification = fixture.CreateContext();
        Assert.Equal(
            outboxBefore,
            await CountOutboxAsync(verification, account, ApplicationEventTypes.PortfolioUpdated));
    }

    [Fact]
    public async Task Parallel_scopes_preserve_locks_cas_and_generation_fencing_with_rest_evidence()
    {
        var (account, generation) = await CreateObservedAccountAsync();
        var seedClock = new MutableTimeProvider(T1);
        var provider = new TestPrivateProvider
        {
            Clock = seedClock,
            PositionsObservedAt = T1,
            BeforePositionsResponse = () =>
            {
                seedClock.UtcNow = T1.AddSeconds(1);
                return Task.CompletedTask;
            },
        };
        provider.SetPosition(OpenPosition(size: 1m));
        await SynchronizeViaScopeAsync(account, provider, seedClock);

        // WS внутри первого REST-интервала: запоздалый size=2 не должен откатить состояние.
        using var ready = new Barrier(2);
        await using var services = CreateApplicationServices(
            account,
            provider,
            new FixedTimeProvider(T1.AddMinutes(2)));
        var syncTask = Task.Run(async () =>
        {
            ready.SignalAndWait();
            await using var scope = services.CreateAsyncScope();
            provider.SetPosition(OpenPosition(size: 3m));
            provider.PositionsObservedAt = T1.AddMinutes(1);
            provider.ObservedAt = T1.AddMinutes(1);
            provider.Clock = new MutableTimeProvider(T1.AddMinutes(1));
            return await scope.ServiceProvider
                .GetRequiredService<ExchangeAccountSyncService>()
                .SynchronizeAsync(account.UserId, account.Id);
        });
        var wsTask = Task.Run(async () =>
        {
            ready.SignalAndWait();
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider
                .GetRequiredService<RealtimePositionReconciliationService>()
                .ApplyAsync(
                    account.UserId,
                    generation,
                    OpenObservation(account, T1.AddMilliseconds(500), 2, OpenPosition(size: 2m)));
        });

        await syncTask.WaitAsync(CompletionTimeout);
        var ws = await wsTask.WaitAsync(CompletionTimeout);

        await using var verification = fixture.CreateContext();
        var position = await SinglePositionAsync(verification, account);
        Assert.NotEqual(2m, position.Size);
        Assert.True(position.Size is 1m or 3m);
        Assert.True(
            ws.Outcome is RealtimeObservationApplyOutcome.RecoveryRequired
                or RealtimeObservationApplyOutcome.Applied);
        if (ws.Outcome == RealtimeObservationApplyOutcome.Applied)
        {
            Assert.True(ws.ConfirmedByRest);
        }
    }

    private async Task<ExchangeAccount> CreateAccountWithoutObservationAsync()
    {
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From($"provider-{Guid.NewGuid():N}"),
            "REST evidence",
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        await using var context = fixture.CreateContext();
        await new ExchangeAccountRepository(context).SaveAsync(account.UserId, account, expectedVersion: null);
        return account;
    }

    private async Task<ExchangeAccountSyncResult> SynchronizeViaScopeAsync(
        ExchangeAccount account,
        TestPrivateProvider provider,
        TimeProvider clock)
    {
        await using var services = CreateApplicationServices(account, provider, clock);
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ExchangeAccountSyncService>()
            .SynchronizeAsync(account.UserId, account.Id);
    }

    private async Task<bool> HasRestOrderingKindAsync(
        ExchangeAccount account,
        ExchangeRestOrderingKind kind,
        string? symbol = null)
    {
        await using var context = fixture.CreateContext();
        var query = context.ExchangeRestOrderingIntervals.AsNoTracking()
            .Where(interval =>
                interval.ExchangeAccountId == account.Id.Value &&
                interval.Kind == kind);
        if (symbol is not null)
        {
            query = query.Where(interval => interval.Symbol == symbol);
        }

        return await query.AnyAsync();
    }

    /// <summary>
    /// Моделирует потерю/отсутствие interval history после принятого REST без production delete path.
    /// </summary>
    private async Task ClearRestOrderingEvidenceAsync(ExchangeAccount account)
    {
        await using var context = fixture.CreateContext();
        var intervals = await context.ExchangeRestOrderingIntervals
            .Where(interval => interval.ExchangeAccountId == account.Id.Value)
            .ToListAsync();
        context.ExchangeRestOrderingIntervals.RemoveRange(intervals);
        await context.SaveChangesAsync();
    }
}
