using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Accounts.Access;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

/// <summary>
/// Покрытие PostgreSQL для lifecycle boundary биржевого аккаунта F-02:
/// user-scoped запрос списка активных аккаунтов и атомарность lifecycle-транзакции
/// ротации credentials (<see cref="ExchangeAccountLifecycleTransaction"/>).
/// </summary>
[Collection("PostgreSql-A")]
public sealed class ExchangeAccountLifecyclePostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Connect_persists_account_credentials_and_invalidation_in_one_lifecycle()
    {
        var userId = UserId.New();
        var keys = CreateKeys("v1");
        var credentials = new ExchangeAccountCredentialSecret("api-key", "api-secret");

        await using (var context = fixture.CreateContext())
        {
            var service = new ExchangeAccountService(
                new FixedAccessVerifier(ExchangeAccountProviderIdentity.From("provider-account")),
                new ExchangeAccountRepository(context),
                CreateStore(context, keys),
                new ExchangeAccountLifecycleTransaction(context),
                new ApplicationEventOutbox(context));

            var result = await service.ConnectAsync(userId, ExchangeId.Bybit, credentials);

            Assert.Equal(ExchangeAccountConnectionOutcome.Connected, result.Outcome);
            Assert.NotNull(result.Account);
        }

        await using var verificationContext = fixture.CreateContext();
        var account = await verificationContext.ExchangeAccounts
            .SingleAsync(entity => entity.UserId == userId.Value);
        var storedEvents = await verificationContext.OutboxMessages
            .Where(message => message.EventType == ApplicationEventTypes.ExchangeAccountUpdated)
            .ToArrayAsync();
        var storedEvent = storedEvents.Single(
            message => message.Payload.Contains(
                userId.Value.ToString(),
                StringComparison.Ordinal));
        var applicationEvent = ApplicationEventSerializer.Deserialize(
            storedEvent.EventType,
            storedEvent.SchemaVersion,
            storedEvent.Payload);
        var accountEvent = Assert.IsType<ExchangeAccountUpdatedEventV1>(applicationEvent);
        Assert.Equal(userId.Value, accountEvent.UserId);
        Assert.Equal(account.Id, accountEvent.ExchangeAccountId);
        Assert.Equal(1, await verificationContext.ExchangeAccountCredentials.CountAsync(
            credential => credential.ExchangeAccountId == account.Id));
    }

    [Fact]
    public async Task Concurrent_rotation_and_disconnect_serialize_account_before_credential_without_deadlock()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var keys = CreateKeys("v1");
        var original = new ExchangeAccountCredentialSecret("original-key", "original-secret");
        var replacement = new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret");

        await using (var setup = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setup)
                .SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setup, keys).CreateAsync(userId, account.Id, original);
        }

        using var transactionBoundary = new Barrier(2);
        var rotation = RunRotationAsync(
            userId,
            account,
            replacement,
            keys,
            transactionBoundary);
        var disconnect = RunDisconnectAsync(
            userId,
            account,
            keys,
            transactionBoundary);

        await Task
            .WhenAll(rotation, disconnect)
            .WaitAsync(TimeSpan.FromSeconds(30));
        var rotationOutcome = await rotation;
        var disconnectOutcome = await disconnect;

        foreach (var error in new[] { rotationOutcome.Error, disconnectOutcome.Error }.Where(error => error is not null))
            Assert.IsType<ConcurrencyConflictException>(error);

        var rotationWon =
            rotationOutcome.Error is null &&
            rotationOutcome.Result?.Outcome == ExchangeAccountCredentialRotationOutcome.Succeeded;
        var disconnectWon =
            disconnectOutcome.Error is null &&
            disconnectOutcome.Result is not null;
        Assert.True(rotationWon ^ disconnectWon);

        await using var verificationContext = fixture.CreateContext();
        var persistedAccount = await new ExchangeAccountRepository(verificationContext)
            .GetByIdAsync(userId, account.Id);
        Assert.NotNull(persistedAccount);
        var persistedCredential = await CreateStore(verificationContext, keys)
            .GetAsync(userId, account.Id);
        var lifecycleEvents = (await verificationContext.OutboxMessages
                .Where(message => message.EventType == ApplicationEventTypes.ExchangeAccountUpdated)
                .ToArrayAsync())
            .Where(message => message.Payload.Contains(
                account.Id.Value.ToString(),
                StringComparison.Ordinal))
            .ToArray();
        Assert.Single(lifecycleEvents);

        if (rotationWon)
        {
            Assert.Equal(
                ExchangeAccountConnectionStatus.Connected,
                persistedAccount!.Value.ConnectionStatus);
            Assert.NotNull(persistedCredential);
            persistedCredential!.Use((apiKey, apiSecret) =>
            {
                replacement.Use((replacementApiKey, replacementApiSecret) =>
                {
                    Assert.Equal(replacementApiKey, apiKey);
                    Assert.Equal(replacementApiSecret, apiSecret);
                });
            });
        }
        else
        {
            Assert.Equal(
                ExchangeAccountConnectionStatus.Disabled,
                persistedAccount!.Value.ConnectionStatus);
            Assert.Null(persistedCredential);
        }
    }

    private async Task<(
        ExchangeAccountCredentialRotationResult? Result,
        Exception? Error)> RunRotationAsync(
        UserId userId,
        ExchangeAccount account,
        ExchangeAccountCredentialSecret replacement,
        IReadOnlyDictionary<string, string> keys,
        Barrier transactionBoundary)
    {
        await Task.Yield();
        await using var context = fixture.CreateContext();
        return await CaptureAsync(async () =>
        {
            var service = new ExchangeAccountService(
                new FixedAccessVerifier(account.ProviderIdentity),
                new ExchangeAccountRepository(context),
                CreateStore(context, keys),
                new CoordinatedLifecycleTransaction(
                    new ExchangeAccountLifecycleTransaction(context),
                    transactionBoundary),
                new ApplicationEventOutbox(context));
            return await service.RotateCredentialsAsync(userId, account.Id, replacement);
        });
    }

    private async Task<(ExchangeAccount? Result, Exception? Error)> RunDisconnectAsync(
        UserId userId,
        ExchangeAccount account,
        IReadOnlyDictionary<string, string> keys,
        Barrier transactionBoundary)
    {
        await Task.Yield();
        await using var context = fixture.CreateContext();
        return await CaptureAsync(async () =>
        {
            var service = new ExchangeAccountService(
                new FixedAccessVerifier(account.ProviderIdentity),
                new ExchangeAccountRepository(context),
                CreateStore(context, keys),
                new CoordinatedLifecycleTransaction(
                    new ExchangeAccountLifecycleTransaction(context),
                    transactionBoundary),
                new ApplicationEventOutbox(context));
            return await service.DisconnectAsync(userId, account.Id);
        });
    }

    [Fact]
    public async Task ListActiveAsync_returns_only_the_owning_users_non_disabled_accounts_in_deterministic_id_order()
    {
        var owner = UserId.New();
        var otherUser = UserId.New();
        var first = CreateAccount(owner);
        var second = CreateAccount(owner);
        var disabled = CreateAccount(owner, ExchangeAccountConnectionStatus.Disabled);
        var foreign = CreateAccount(otherUser);

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            await repository.SaveAsync(owner, first, expectedVersion: null);
            await repository.SaveAsync(owner, second, expectedVersion: null);
            await repository.SaveAsync(owner, disabled, expectedVersion: null);
            await repository.SaveAsync(otherUser, foreign, expectedVersion: null);
        }

        await using var readContext = fixture.CreateContext();
        var result = await new ExchangeAccountRepository(readContext).ListActiveAsync(owner);

        var expectedOrder = new[] { first.Id, second.Id }.OrderBy(id => id.Value).ToArray();
        Assert.Equal(expectedOrder, result.Select(x => x.Value.Id).ToArray());
        Assert.DoesNotContain(result, x => x.Value.Id == disabled.Id);
        Assert.DoesNotContain(result, x => x.Value.Id == foreign.Id);
    }

    [Fact]
    public async Task Provider_identity_round_trips_and_is_not_nullable_in_PostgreSql()
    {
        var userId = UserId.New();
        var providerIdentity = ExchangeAccountProviderIdentity.From("bybit-user-123456");
        var account = ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            providerIdentity);

        await using var context = fixture.CreateContext();
        await new ExchangeAccountRepository(context).SaveAsync(userId, account, expectedVersion: null);

        var reloaded = await new ExchangeAccountRepository(context).GetByIdAsync(userId, account.Id);
        Assert.Equal(providerIdentity, reloaded!.Value.ProviderIdentity);

        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT is_nullable, data_type, character_maximum_length
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'exchange_accounts'
              AND column_name = 'provider_account_id';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("NO", reader.GetString(0));
        Assert.Equal("character varying", reader.GetString(1));
        Assert.Equal(128, reader.GetInt32(2));
    }

    [Fact]
    public async Task SaveAsync_rejects_provider_identity_rebinding_and_preserves_the_existing_row()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(
                userId,
                account,
                expectedVersion: null);
        }

        var rebound = ExchangeAccount.Create(
            account.Id,
            userId,
            account.ExchangeId,
            ExchangeAccountProviderIdentity.From("different-provider-account"),
            ExchangeAccountConnectionStatus.Unavailable,
            account.Capabilities,
            lastError: "attempted provider rebinding");

        await using (var updateContext = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => new ExchangeAccountRepository(updateContext).SaveAsync(
                    userId,
                    rebound,
                    ConcurrencyVersion.Initial));
        }

        await using var readContext = fixture.CreateContext();
        var persisted = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ConcurrencyVersion.Initial, persisted!.Version);
        Assert.Equal(account.ProviderIdentity, persisted.Value.ProviderIdentity);
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, persisted.Value.ConnectionStatus);
        Assert.Null(persisted.Value.LastError);
        Assert.Equal(account.Capabilities, persisted.Value.Capabilities);
    }

    [Fact]
    public async Task Real_exchange_account_service_rejects_identity_mismatch_without_mutating_postgres()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId, ExchangeAccountConnectionStatus.Connected);
        var keys = CreateKeys("v1");
        var replacement = new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret");

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId,
                account.Id,
                new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var service = new ExchangeAccountService(
                new FixedAccessVerifier(ExchangeAccountProviderIdentity.From("different-bybit-user")),
                repository,
                store,
                new ExchangeAccountLifecycleTransaction(context),
                new ApplicationEventOutbox(context));

            var result = await service.RotateCredentialsAsync(userId, account.Id, replacement);

            Assert.Equal(
                ExchangeAccountCredentialRotationOutcome.ProviderIdentityMismatch,
                result.Outcome);
            Assert.Null(result.Account);
        }

        await using var readContext = fixture.CreateContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ConcurrencyVersion.Initial, reloadedAccount!.Version);
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, reloadedAccount.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("original-key", apiKey);
            Assert.Equal("original-secret", apiSecret);
        });
        Assert.Equal(ConcurrencyVersion.Initial, reloadedCredential.Version);
    }

    [Fact]
    public async Task Real_exchange_account_service_rejects_stale_rotation_after_competing_account_update()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var keys = CreateKeys("v1");
        var verifier = new GatedAccessVerifier(account.ProviderIdentity);

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId,
                account.Id,
                new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        var rotationTask = Task.Run(async () =>
        {
            await using var context = fixture.CreateContext();
            var service = new ExchangeAccountService(
                verifier,
                new ExchangeAccountRepository(context),
                CreateStore(context, keys),
                new ExchangeAccountLifecycleTransaction(context),
                new ApplicationEventOutbox(context));

            return await service.RotateCredentialsAsync(
                userId,
                account.Id,
                new ExchangeAccountCredentialSecret("replacement-key", "replacement-secret"));
        });

        await verifier.VerificationStarted;

        await using (var competingContext = fixture.CreateContext())
        {
            var competingAccount = (await new ExchangeAccountRepository(competingContext)
                .GetByIdAsync(userId, account.Id))!.Value;
            competingAccount.MarkUnavailable("competing update");
            await new ExchangeAccountRepository(competingContext)
                .SaveAsync(userId, competingAccount, new ConcurrencyVersion(1));
        }

        verifier.ReleaseVerification();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () => { await rotationTask; });

        await using var readContext = fixture.CreateContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(new ConcurrencyVersion(2), reloadedAccount!.Version);
        Assert.Equal(ExchangeAccountConnectionStatus.Unavailable, reloadedAccount.Value.ConnectionStatus);
        Assert.Equal("competing update", reloadedAccount.Value.LastError);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("original-key", apiKey);
            Assert.Equal("original-secret", apiSecret);
        });
        Assert.Equal(ConcurrencyVersion.Initial, reloadedCredential.Version);
    }

    [Fact]
    public async Task Lifecycle_transaction_commits_credential_and_account_changes_together_on_success()
    {
        var userId = UserId.New();
        var account = CreateAccount(userId, ExchangeAccountConnectionStatus.Unavailable);
        var keys = CreateKeys("v1");

        ConcurrencyVersion accountVersion;
        await using (var setupContext = fixture.CreateContext())
        {
            accountVersion = await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        await using (var context = fixture.CreateContext())
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

        await using var readContext = fixture.CreateContext();
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

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        // Параллельный writer увеличивает version аккаунта между чтением до верификации
        // и lifecycle-транзакцией, поэтому CAS-сохранение аккаунта ниже видит устаревшую
        // expected version и завершается ошибкой после ротации credentials в той же транзакции.
        await using (var concurrentWriterContext = fixture.CreateContext())
        {
            var concurrentAccount = CreateAccount(account.Id, userId, ExchangeAccountConnectionStatus.Unavailable);
            await new ExchangeAccountRepository(concurrentWriterContext).SaveAsync(
                userId, concurrentAccount, ConcurrencyVersion.Initial);
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var transaction = new ExchangeAccountLifecycleTransaction(context);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => transaction.ExecuteAsync(async token =>
            {
                await store.RotateAsync(userId, account.Id, ConcurrencyVersion.Initial,
                    new ExchangeAccountCredentialSecret("rotated-key", "rotated-secret"), token);
                account.MarkConnected();
                // Устаревшая expected version (1): параллельный writer уже увеличил её до 2.
                await repository.SaveAsync(userId, account, ConcurrencyVersion.Initial, token);
            }));
        }

        await using var readContext = fixture.CreateContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Unavailable, reloadedAccount!.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, apiSecret) =>
        {
            // Ротация credentials внутри неудачной транзакции должна откатиться вместе
            // с сохранением аккаунта: исходная пара всё ещё активна.
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

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, account, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId, account.Id, new ExchangeAccountCredentialSecret("original-key", "original-secret"));
        }

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            var store = CreateStore(context, keys);
            var transaction = new ExchangeAccountLifecycleTransaction(context);
            var staleCredentialVersion = new ConcurrencyVersion(2);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => transaction.ExecuteAsync(async token =>
            {
                // Устаревшая expected credential version: строка всё ещё имеет version 1.
                await store.RotateAsync(userId, account.Id, staleCredentialVersion,
                    new ExchangeAccountCredentialSecret("rotated-key", "rotated-secret"), token);
                account.MarkConnected();
                await repository.SaveAsync(userId, account, ConcurrencyVersion.Initial, token);
            }));
        }

        await using var readContext = fixture.CreateContext();
        var reloadedAccount = await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, account.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Unavailable, reloadedAccount!.Value.ConnectionStatus);
        var reloadedCredential = await CreateStore(readContext, keys).GetAsync(userId, account.Id);
        reloadedCredential!.Use((apiKey, _) => Assert.Equal("original-key", apiKey));
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
            ExchangeAccountId.New(), userId, ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"), status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static async Task<(T? Result, Exception? Error)> CaptureAsync<T>(
        Func<Task<T>> operation)
    {
        try
        {
            return (await operation(), null);
        }
        catch (Exception exception)
        {
            return (default, exception);
        }
    }

    private sealed class CoordinatedLifecycleTransaction(
        IExchangeAccountLifecycleTransaction inner,
        Barrier barrier) : IExchangeAccountLifecycleTransaction
    {
        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            barrier.SignalAndWait(cancellationToken);
            await inner.ExecuteAsync(operation, cancellationToken);
        }
    }

    private sealed class FixedAccessVerifier(ExchangeAccountProviderIdentity providerIdentity)
        : IExchangeAccountAccessVerifier
    {
        public Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
            ExchangeId exchange,
            ExchangeAccountCredentialSecret credentials,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExchangeAccountAccessVerificationResult.Verified(
                providerIdentity,
                ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions));
    }

    private sealed class GatedAccessVerifier(ExchangeAccountProviderIdentity providerIdentity)
        : IExchangeAccountAccessVerifier
    {
        private readonly TaskCompletionSource<bool> verificationStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> verificationReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task VerificationStarted => verificationStarted.Task;

        public async Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
            ExchangeId exchange,
            ExchangeAccountCredentialSecret credentials,
            CancellationToken cancellationToken = default)
        {
            verificationStarted.TrySetResult(true);
            await verificationReleased.Task.WaitAsync(cancellationToken);
            return ExchangeAccountAccessVerificationResult.Verified(
                providerIdentity,
                ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
        }

        public void ReleaseVerification() => verificationReleased.TrySetResult(true);
    }

    private static ExchangeAccount CreateAccount(
        ExchangeAccountId id,
        UserId userId,
        ExchangeAccountConnectionStatus status) =>
        ExchangeAccount.Create(
            id, userId, ExchangeId.Bybit, ExchangeAccountProviderIdentity.From("provider-account"), status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);
}
