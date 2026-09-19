using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

/// <summary>
/// PostgreSQL-backed coverage for the F-02 exchange-account lifecycle boundary: the
/// user-scoped active-account list query and the atomicity of the credential-rotation
/// lifecycle transaction (<see cref="ExchangeAccountLifecycleTransaction"/>).
/// </summary>
[Collection("PostgreSql")]
public sealed class ExchangeAccountLifecyclePostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ListActiveAsync_returns_only_the_owning_users_non_disabled_accounts_in_deterministic_id_order()
    {
        var owner = UserId.New();
        var otherUser = UserId.New();
        var first = CreateAccount(owner);
        var second = CreateAccount(owner);
        var disabled = CreateAccount(owner, ExchangeAccountConnectionStatus.Disabled);
        var foreign = CreateAccount(otherUser);

        await using (var context = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(context);
            await repository.SaveAsync(owner, first, expectedVersion: null);
            await repository.SaveAsync(owner, second, expectedVersion: null);
            await repository.SaveAsync(owner, disabled, expectedVersion: null);
            await repository.SaveAsync(otherUser, foreign, expectedVersion: null);
        }

        await using var readContext = await CreateMigratedContext();
        var result = await new ExchangeAccountRepository(readContext).ListActiveAsync(owner);

        var expectedOrder = new[] { first.Id, second.Id }.OrderBy(id => id.Value).ToArray();
        Assert.Equal(expectedOrder, result.Select(x => x.Value.Id).ToArray());
        Assert.DoesNotContain(result, x => x.Value.Id == disabled.Id);
        Assert.DoesNotContain(result, x => x.Value.Id == foreign.Id);
    }

    [Fact]
    public async Task Lifecycle_transaction_commits_credential_and_account_changes_together_on_success()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId, ExchangeAccountConnectionStatus.Unavailable);
        var keys = CreateKeys("v1");

        ConcurrencyVersion accountVersion;
        await using (var setupContext = await CreateMigratedContext())
        {
            accountVersion = await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        await using (var context = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var transaction = new ExchangeAccountLifecycleTransaction(context);

            await transaction.ExecuteAsync(async token =>
            {
                await store.RotateAsync(userId, account.Id, ConcurrencyVersion.Initial,
                    new ExchangeAccountCredentialSecret("rotated-key", "rotated-secret"), token);
                account.MarkConnected();
                await repository.SaveAsync(userId, account, accountVersion, token);
            });
        }

        await using var readContext = await CreateMigratedContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, reloadedAccount!.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("rotated-key", apiKey);
            Assert.Equal("rotated-secret", apiSecret);
        });
    }

    [Fact]
    public async Task Lifecycle_transaction_rolls_back_the_credential_rotation_when_the_subsequent_account_save_fails()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId, ExchangeAccountConnectionStatus.Unavailable);
        var keys = CreateKeys("v1");

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        // A concurrent writer bumps the account's version between the pre-verification read
        // and the lifecycle transaction, so the account CAS save below observes a stale
        // expected version and fails after the credential has already been rotated in the
        // same transaction.
        await using (var concurrentWriterContext = await CreateMigratedContext())
        {
            var concurrentAccount = CreateAccount(account.Id, userId, ExchangeAccountConnectionStatus.Unavailable);
            await new ExchangeAccountRepository(concurrentWriterContext).SaveAsync(
                userId, concurrentAccount, ConcurrencyVersion.Initial);
        }

        await using (var context = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var transaction = new ExchangeAccountLifecycleTransaction(context);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => transaction.ExecuteAsync(async token =>
            {
                await store.RotateAsync(userId, account.Id, ConcurrencyVersion.Initial,
                    new ExchangeAccountCredentialSecret("rotated-key", "rotated-secret"), token);
                account.MarkConnected();
                // Stale expected version (1): the concurrent writer already advanced it to 2.
                await repository.SaveAsync(userId, account, ConcurrencyVersion.Initial, token);
            }));
        }

        await using var readContext = await CreateMigratedContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Unavailable, reloadedAccount!.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, apiSecret) =>
        {
            // The credential rotation performed inside the failed transaction must have been
            // rolled back together with the account save: the original pair is still active.
            Assert.Equal("original-key", apiKey);
            Assert.Equal("original-secret", apiSecret);
        });
    }

    [Fact]
    public async Task Lifecycle_transaction_leaves_no_partial_state_when_the_credential_cas_itself_fails()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId, ExchangeAccountConnectionStatus.Unavailable);
        var keys = CreateKeys("v1");

        await using (var setupContext = await CreateMigratedContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        await using (var context = await CreateMigratedContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var transaction = new ExchangeAccountLifecycleTransaction(context);
            var staleCredentialVersion = new ConcurrencyVersion(2);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => transaction.ExecuteAsync(async token =>
            {
                // Stale expected credential version: the row is still at version 1.
                await store.RotateAsync(userId, account.Id, staleCredentialVersion,
                    new ExchangeAccountCredentialSecret("rotated-key", "rotated-secret"), token);
                account.MarkConnected();
                await repository.SaveAsync(userId, account, ConcurrencyVersion.Initial, token);
            }));
        }

        await using var readContext = await CreateMigratedContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Unavailable, reloadedAccount!.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, _) => Assert.Equal("original-key", apiKey));
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static ExchangeAccountCredentialStore CreateStore(
        TradeSystemDbContext context,
        IReadOnlyDictionary<string, string> keys) =>
        new(context, new AesGcmExchangeCredentialProtector(
            CredentialKeyRing.Create(new CredentialProtectionOptions { ActiveKeyId = "v1", Keys = keys })));

    private static Dictionary<string, string> CreateKeys(params string[] ids) =>
        ids.ToDictionary(
            id => id,
            _ => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            StringComparer.Ordinal);

    private static ExchangeAccount CreateAccount(
        UserId userId,
        ExchangeAccountConnectionStatus status = ExchangeAccountConnectionStatus.Connected) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(), userId, ExchangeId.Bybit, status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static ExchangeAccount CreateAccount(
        ExchangeAccountId id,
        UserId userId,
        ExchangeAccountConnectionStatus status) =>
        ExchangeAccount.Create(
            id, userId, ExchangeId.Bybit, status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
}
