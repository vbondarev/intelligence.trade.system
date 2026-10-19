using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

public sealed partial class RealtimeReconciliationPostgreSqlTests
{
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
}
