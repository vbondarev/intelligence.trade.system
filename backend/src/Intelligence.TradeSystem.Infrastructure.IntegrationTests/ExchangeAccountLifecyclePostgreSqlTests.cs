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
/// Покрытие PostgreSQL для lifecycle boundary биржевого аккаунта:
/// user-scoped запрос списка активных аккаунтов, атомарность lifecycle-транзакции
/// ротации credentials (<see cref="ExchangeAccountLifecycleTransaction"/>), а также
/// инвариант <c>UserId + ExchangeId + ProviderIdentity → один ExchangeAccountId</c>
/// при connect, reconnect отключённого аккаунта и конкурентных попытках подключения.
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
        var first = CreateAccount(owner, providerIdentity: UniqueProviderIdentity());
        var second = CreateAccount(owner, providerIdentity: UniqueProviderIdentity());
        var disabled = CreateAccount(
            owner,
            ExchangeAccountConnectionStatus.Disabled,
            UniqueProviderIdentity());
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

    [Fact]
    public async Task Provider_account_unique_index_covers_user_exchange_and_provider_identity_without_status_filter()
    {
        await using var context = fixture.CreateContext();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT index_info.indisunique,
                   index_info.indpred IS NULL,
                   array_to_string(ARRAY(
                       SELECT attribute.attname
                       FROM unnest(index_info.indkey) WITH ORDINALITY AS key(attnum, position)
                       JOIN pg_attribute AS attribute
                         ON attribute.attrelid = index_info.indrelid
                        AND attribute.attnum = key.attnum
                       ORDER BY key.position), ',')
            FROM pg_index AS index_info
            JOIN pg_class AS index_class ON index_class.oid = index_info.indexrelid
            WHERE index_class.relname = 'ux_exchange_accounts_user_exchange_provider_account_id';
            """;
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal("user_id,exchange_id,provider_account_id", reader.GetString(2));
    }

    [Fact]
    public async Task SaveAsync_rejects_a_second_account_for_the_same_user_exchange_and_provider_identity()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var first = CreateAccount(userId, ExchangeAccountConnectionStatus.Disabled, providerIdentity);
        var duplicate = CreateAccount(userId, providerIdentity: providerIdentity);

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, first, expectedVersion: null);
        }

        await using (var duplicateContext = fixture.CreateContext())
        {
            var conflict = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => new ExchangeAccountRepository(duplicateContext)
                    .SaveAsync(userId, duplicate, expectedVersion: null));
            Assert.DoesNotContain(providerIdentity.Value, conflict.Message, StringComparison.Ordinal);
        }

        await using var readContext = fixture.CreateContext();
        var persisted = await readContext.ExchangeAccounts
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync();
        Assert.Equal([first.Id.Value], persisted);
    }

    [Fact]
    public async Task SaveAsync_allows_master_and_subaccounts_of_one_exchange_for_the_same_user()
    {
        var userId = UserId.New();
        var master = CreateAccount(userId, providerIdentity: UniqueProviderIdentity());
        var subaccountA = CreateAccount(userId, providerIdentity: UniqueProviderIdentity());
        var subaccountB = CreateAccount(userId, providerIdentity: UniqueProviderIdentity());

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            await repository.SaveAsync(userId, master, expectedVersion: null);
            await repository.SaveAsync(userId, subaccountA, expectedVersion: null);
            await repository.SaveAsync(userId, subaccountB, expectedVersion: null);
        }

        await using var readContext = fixture.CreateContext();
        Assert.Equal(3, await readContext.ExchangeAccounts.CountAsync(account => account.UserId == userId.Value));
    }

    [Fact]
    public async Task SaveAsync_allows_the_same_provider_identity_for_different_users()
    {
        var providerIdentity = UniqueProviderIdentity();
        var firstUser = UserId.New();
        var secondUser = UserId.New();
        var firstAccount = CreateAccount(firstUser, providerIdentity: providerIdentity);
        var secondAccount = CreateAccount(secondUser, providerIdentity: providerIdentity);

        await using (var context = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(context);
            await repository.SaveAsync(firstUser, firstAccount, expectedVersion: null);
            await repository.SaveAsync(secondUser, secondAccount, expectedVersion: null);
        }

        await using var readContext = fixture.CreateContext();
        var repositoryReader = new ExchangeAccountRepository(readContext);
        Assert.Equal(
            firstAccount.Id,
            (await repositoryReader.GetByProviderIdentityAsync(firstUser, ExchangeId.Bybit, providerIdentity))!.Value.Id);
        Assert.Equal(
            secondAccount.Id,
            (await repositoryReader.GetByProviderIdentityAsync(secondUser, ExchangeId.Bybit, providerIdentity))!.Value.Id);
    }

    [Fact]
    public async Task GetByProviderIdentityAsync_is_user_scoped_and_returns_disabled_accounts()
    {
        var owner = UserId.New();
        var foreignUser = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var disabled = CreateAccount(owner, ExchangeAccountConnectionStatus.Disabled, providerIdentity);
        var otherProviderAccount = CreateAccount(owner, providerIdentity: UniqueProviderIdentity());

        await using (var setupContext = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(setupContext);
            await repository.SaveAsync(owner, disabled, expectedVersion: null);
            await repository.SaveAsync(owner, otherProviderAccount, expectedVersion: null);
        }

        await using var readContext = fixture.CreateContext();
        var reader = new ExchangeAccountRepository(readContext);
        var found = await reader.GetByProviderIdentityAsync(owner, ExchangeId.Bybit, providerIdentity);

        Assert.NotNull(found);
        Assert.Equal(disabled.Id, found!.Value.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Disabled, found.Value.ConnectionStatus);
        Assert.Equal(providerIdentity, found.Value.ProviderIdentity);
        Assert.Equal(ConcurrencyVersion.Initial, found.Version);
        Assert.Null(await reader.GetByProviderIdentityAsync(foreignUser, ExchangeId.Bybit, providerIdentity));
        Assert.Null(await reader.GetByProviderIdentityAsync(owner, ExchangeId.Bybit, UniqueProviderIdentity()));
    }

    [Fact]
    public async Task Connect_after_disconnect_restores_the_same_account_with_new_credentials_and_history()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        var syncedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        ExchangeAccountId accountId;

        await using (var context = fixture.CreateContext())
        {
            var result = await CreateService(context, keys, new FixedAccessVerifier(providerIdentity))
                .ConnectAsync(userId, ExchangeId.Bybit, new ExchangeAccountCredentialSecret("old-key", "old-secret"));
            Assert.Equal(ExchangeAccountConnectionOutcome.Connected, result.Outcome);
            accountId = result.Account!.Id;
        }

        await using (var historyContext = fixture.CreateContext())
        {
            var repository = new ExchangeAccountRepository(historyContext);
            var loaded = (await repository.GetByIdAsync(userId, accountId))!;
            loaded.Value.AdvanceObservationWatermark(ExchangeAccountObservationResource.Balance, syncedAt);
            loaded.Value.AdvanceObservationWatermark(ExchangeAccountObservationResource.Positions, syncedAt);
            loaded.Value.RecordSuccessfulSync(syncedAt);
            await repository.SaveAsync(userId, loaded.Value, loaded.Version);
        }

        await using (var context = fixture.CreateContext())
        {
            Assert.NotNull(await CreateService(context, keys, new FixedAccessVerifier(providerIdentity))
                .DisconnectAsync(userId, accountId));
        }

        ExchangeAccountConnectionResult reconnect;
        await using (var context = fixture.CreateContext())
        {
            reconnect = await CreateService(context, keys, new FixedAccessVerifier(providerIdentity))
                .ConnectAsync(userId, ExchangeId.Bybit, new ExchangeAccountCredentialSecret("new-key", "new-secret"));
        }

        Assert.Equal(ExchangeAccountConnectionOutcome.Reconnected, reconnect.Outcome);
        Assert.Equal(accountId, reconnect.Account!.Id);

        await using var readContext = fixture.CreateContext();
        var accountIds = await readContext.ExchangeAccounts
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync();
        Assert.Equal([accountId.Value], accountIds);
        var persisted = (await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, accountId))!;
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, persisted.Value.ConnectionStatus);
        Assert.Equal(providerIdentity, persisted.Value.ProviderIdentity);
        Assert.Equal(syncedAt, persisted.Value.LastSyncedAt);
        Assert.Equal(syncedAt, persisted.Value.LastAppliedBalanceObservationAt);
        Assert.Equal(syncedAt, persisted.Value.LastAppliedPositionsObservationAt);
        Assert.Null(persisted.Value.LastError);
        var credential = await CreateStore(readContext, keys).GetAsync(userId, accountId);
        credential!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("new-key", apiKey);
            Assert.Equal("new-secret", apiSecret);
        });
        Assert.Equal(3, await CountAccountEventsAsync(readContext, accountId));
    }

    [Theory]
    [InlineData(ExchangeAccountAccessVerificationStatus.InvalidCredentials)]
    [InlineData(ExchangeAccountAccessVerificationStatus.PermissionsRejected)]
    [InlineData(ExchangeAccountAccessVerificationStatus.Unavailable)]
    public async Task Failed_reconnect_verification_leaves_the_disabled_account_unchanged(
        ExchangeAccountAccessVerificationStatus verificationStatus)
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        ExchangeAccountId accountId;

        await using (var context = fixture.CreateContext())
        {
            var service = CreateService(context, keys, new FixedAccessVerifier(providerIdentity));
            var connected = await service.ConnectAsync(
                userId,
                ExchangeId.Bybit,
                new ExchangeAccountCredentialSecret("old-key", "old-secret"));
            accountId = connected.Account!.Id;
            await service.DisconnectAsync(userId, accountId);
        }

        Versioned<ExchangeAccount> before;
        int eventsBefore;
        await using (var snapshotContext = fixture.CreateContext())
        {
            before = (await new ExchangeAccountRepository(snapshotContext).GetByIdAsync(userId, accountId))!;
            eventsBefore = await CountAccountEventsAsync(snapshotContext, accountId);
        }

        await using (var context = fixture.CreateContext())
        {
            var result = await CreateService(context, keys, new FailedAccessVerifier(verificationStatus))
                .ConnectAsync(userId, ExchangeId.Bybit, new ExchangeAccountCredentialSecret("new-key", "new-secret"));
            Assert.NotEqual(ExchangeAccountConnectionOutcome.Reconnected, result.Outcome);
            Assert.Null(result.Account);
        }

        await using var readContext = fixture.CreateContext();
        var after = (await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, accountId))!;
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(ExchangeAccountConnectionStatus.Disabled, after.Value.ConnectionStatus);
        Assert.Equal(providerIdentity, after.Value.ProviderIdentity);
        Assert.Null(await CreateStore(readContext, keys).GetMetadataAsync(userId, accountId));
        Assert.Equal(1, await readContext.ExchangeAccounts.CountAsync(account => account.UserId == userId.Value));
        Assert.Equal(eventsBefore, await CountAccountEventsAsync(readContext, accountId));
    }

    [Fact]
    public async Task Reconnect_rolls_back_the_account_update_when_the_credential_phase_conflicts()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        var disabled = CreateAccount(userId, ExchangeAccountConnectionStatus.Disabled, providerIdentity);

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, disabled, expectedVersion: null);
            await CreateStore(setupContext, keys).CreateAsync(
                userId,
                disabled.Id,
                new ExchangeAccountCredentialSecret("leftover-key", "leftover-secret"));
        }

        await using (var context = fixture.CreateContext())
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => CreateService(context, keys, new FixedAccessVerifier(providerIdentity))
                    .ConnectAsync(userId, ExchangeId.Bybit, new ExchangeAccountCredentialSecret("new-key", "new-secret")));
        }

        await using var readContext = fixture.CreateContext();
        var persisted = (await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, disabled.Id))!;
        Assert.Equal(ConcurrencyVersion.Initial, persisted.Version);
        Assert.Equal(ExchangeAccountConnectionStatus.Disabled, persisted.Value.ConnectionStatus);
        var credential = await CreateStore(readContext, keys).GetAsync(userId, disabled.Id);
        credential!.Use((apiKey, _) => Assert.Equal("leftover-key", apiKey));
        Assert.Equal(ConcurrencyVersion.Initial, credential.Version);
        Assert.Equal(0, await CountAccountEventsAsync(readContext, disabled.Id));
    }

    [Fact]
    public async Task Concurrent_first_connects_of_one_provider_account_create_a_single_account_without_deadlock()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        using var lookupBoundary = new Barrier(2);

        var first = RunConnectAsync(
            userId, providerIdentity, new ExchangeAccountCredentialSecret("first-key", "first-secret"), keys, lookupBoundary);
        var second = RunConnectAsync(
            userId, providerIdentity, new ExchangeAccountCredentialSecret("second-key", "second-secret"), keys, lookupBoundary);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        var outcomes = new[] { await first, await second };

        var winner = Assert.Single(outcomes, outcome =>
            outcome.Error is null && outcome.Result?.Outcome == ExchangeAccountConnectionOutcome.Connected);
        var loser = Assert.Single(outcomes, outcome => outcome.Error is not null);
        Assert.IsType<ConcurrencyConflictException>(loser.Error);
        var accountId = winner.Result!.Account!.Id;

        await using var readContext = fixture.CreateContext();
        var accountIds = await readContext.ExchangeAccounts
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync();
        Assert.Equal([accountId.Value], accountIds);
        Assert.Equal(1, await readContext.ExchangeAccountCredentials.CountAsync(
            credential => credential.ExchangeAccountId == accountId.Value));
        Assert.Equal(1, await CountAccountEventsAsync(readContext, accountId));
    }

    [Fact]
    public async Task Concurrent_reconnects_of_one_disabled_account_restore_it_once_without_deadlock()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        var disabled = CreateAccount(userId, ExchangeAccountConnectionStatus.Disabled, providerIdentity);
        var firstSecret = new ExchangeAccountCredentialSecret("first-key", "first-secret");
        var secondSecret = new ExchangeAccountCredentialSecret("second-key", "second-secret");

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, disabled, expectedVersion: null);
        }

        using var lookupBoundary = new Barrier(2);
        var first = RunConnectAsync(userId, providerIdentity, firstSecret, keys, lookupBoundary);
        var second = RunConnectAsync(userId, providerIdentity, secondSecret, keys, lookupBoundary);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        var firstOutcome = await first;
        var secondOutcome = await second;

        var firstWon = firstOutcome.Error is null &&
                       firstOutcome.Result?.Outcome == ExchangeAccountConnectionOutcome.Reconnected;
        var secondWon = secondOutcome.Error is null &&
                        secondOutcome.Result?.Outcome == ExchangeAccountConnectionOutcome.Reconnected;
        Assert.True(firstWon ^ secondWon);
        Assert.IsType<ConcurrencyConflictException>(firstWon ? secondOutcome.Error : firstOutcome.Error);
        var expectedApiKey = firstWon ? "first-key" : "second-key";

        await using var readContext = fixture.CreateContext();
        var accountIds = await readContext.ExchangeAccounts
            .Where(account => account.UserId == userId.Value)
            .Select(account => account.Id)
            .ToArrayAsync();
        Assert.Equal([disabled.Id.Value], accountIds);
        var persisted = (await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, disabled.Id))!;
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, persisted.Value.ConnectionStatus);
        Assert.Equal(ConcurrencyVersion.Initial.Next(), persisted.Version);
        Assert.Equal(1, await readContext.ExchangeAccountCredentials.CountAsync(
            credential => credential.ExchangeAccountId == disabled.Id.Value));
        var credential = await CreateStore(readContext, keys).GetAsync(userId, disabled.Id);
        credential!.Use((apiKey, _) => Assert.Equal(expectedApiKey, apiKey));
        Assert.Equal(1, await CountAccountEventsAsync(readContext, disabled.Id));
    }

    [Fact]
    public async Task Stale_disabled_disconnect_cannot_revoke_credentials_of_a_concurrent_reconnect()
    {
        var userId = UserId.New();
        var providerIdentity = UniqueProviderIdentity();
        var keys = CreateKeys("v1");
        var disabled = CreateAccount(userId, ExchangeAccountConnectionStatus.Disabled, providerIdentity);

        await using (var setupContext = fixture.CreateContext())
        {
            await new ExchangeAccountRepository(setupContext).SaveAsync(userId, disabled, expectedVersion: null);
        }

        await using var disconnectContext = fixture.CreateContext();
        var gatedStore = new MetadataGatedCredentialStore(CreateStore(disconnectContext, keys));
        var disconnectService = new ExchangeAccountService(
            new FixedAccessVerifier(providerIdentity),
            new ExchangeAccountRepository(disconnectContext),
            gatedStore,
            new ExchangeAccountLifecycleTransaction(disconnectContext),
            new ApplicationEventOutbox(disconnectContext));
        var disconnect = Task.Run(() => CaptureAsync(() => disconnectService.DisconnectAsync(userId, disabled.Id)));
        await gatedStore.MetadataRequested.WaitAsync(TimeSpan.FromSeconds(30));

        ExchangeAccountConnectionResult reconnect;
        await using (var reconnectContext = fixture.CreateContext())
        {
            reconnect = await CreateService(reconnectContext, keys, new FixedAccessVerifier(providerIdentity))
                .ConnectAsync(userId, ExchangeId.Bybit, new ExchangeAccountCredentialSecret("new-key", "new-secret"));
        }

        Assert.Equal(ExchangeAccountConnectionOutcome.Reconnected, reconnect.Outcome);
        gatedStore.ReleaseMetadata();
        var disconnectOutcome = await disconnect.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsType<ConcurrencyConflictException>(disconnectOutcome.Error);
        Assert.NotNull(gatedStore.ObservedMetadata);
        Assert.Equal(0, gatedStore.RevokeCalls);

        await using var readContext = fixture.CreateContext();
        var persisted = (await new ExchangeAccountRepository(readContext).GetByIdAsync(userId, disabled.Id))!;
        Assert.Equal(disabled.Id, persisted.Value.Id);
        Assert.Equal(ExchangeAccountConnectionStatus.Connected, persisted.Value.ConnectionStatus);
        Assert.Equal(providerIdentity, persisted.Value.ProviderIdentity);
        Assert.Equal(ConcurrencyVersion.Initial.Next(), persisted.Version);
        var credential = await CreateStore(readContext, keys).GetAsync(userId, disabled.Id);
        Assert.NotNull(credential);
        Assert.Equal(gatedStore.ObservedMetadata!.Version, credential.Version);
        credential.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("new-key", apiKey);
            Assert.Equal("new-secret", apiSecret);
        });
        Assert.Equal(1, await CountAccountEventsAsync(readContext, disabled.Id));
    }

    private async Task<(ExchangeAccountConnectionResult? Result, Exception? Error)> RunConnectAsync(
        UserId userId,
        ExchangeAccountProviderIdentity providerIdentity,
        ExchangeAccountCredentialSecret credentials,
        IReadOnlyDictionary<string, string> keys,
        Barrier lookupBoundary)
    {
        await Task.Yield();
        await using var context = fixture.CreateContext();
        return await CaptureAsync(() => CreateService(
                context,
                keys,
                new FixedAccessVerifier(providerIdentity),
                new ProviderLookupBarrierRepository(new ExchangeAccountRepository(context), lookupBoundary))
            .ConnectAsync(userId, ExchangeId.Bybit, credentials));
    }

    private static ExchangeAccountService CreateService(
        TradeSystemDbContext context,
        IReadOnlyDictionary<string, string> keys,
        IExchangeAccountAccessVerifier verifier,
        IExchangeAccountRepository? repository = null) =>
        new(
            verifier,
            repository ?? new ExchangeAccountRepository(context),
            CreateStore(context, keys),
            new ExchangeAccountLifecycleTransaction(context),
            new ApplicationEventOutbox(context));

    private static async Task<int> CountAccountEventsAsync(
        TradeSystemDbContext context,
        ExchangeAccountId accountId) =>
        (await context.OutboxMessages
            .Where(message => message.EventType == ApplicationEventTypes.ExchangeAccountUpdated)
            .Select(message => message.Payload)
            .ToArrayAsync())
        .Count(payload => payload.Contains(accountId.Value.ToString(), StringComparison.Ordinal));

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
        ExchangeAccountConnectionStatus status = ExchangeAccountConnectionStatus.Connected,
        ExchangeAccountProviderIdentity? providerIdentity = null) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(), userId, ExchangeId.Bybit,
            providerIdentity ?? ExchangeAccountProviderIdentity.From("provider-account"), status,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static ExchangeAccountProviderIdentity UniqueProviderIdentity() =>
        ExchangeAccountProviderIdentity.From($"bybit-user-{Guid.NewGuid():N}");

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

    private sealed class FailedAccessVerifier(ExchangeAccountAccessVerificationStatus status)
        : IExchangeAccountAccessVerifier
    {
        public Task<ExchangeAccountAccessVerificationResult> VerifyAsync(
            ExchangeId exchange,
            ExchangeAccountCredentialSecret credentials,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExchangeAccountAccessVerificationResult.Failed(status));
    }

    /// <summary>
    /// Удерживает обе конкурентные операции после lookup по provider identity, чтобы они
    /// гарантированно приняли решение по одному и тому же исходному состоянию.
    /// </summary>
    private sealed class ProviderLookupBarrierRepository(
        IExchangeAccountRepository inner,
        Barrier barrier) : IExchangeAccountRepository
    {
        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListActiveAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.ListActiveAsync(userId, cancellationToken);

        public Task<Versioned<ExchangeAccount>?> GetByIdAsync(
            UserId userId,
            ExchangeAccountId id,
            CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(userId, id, cancellationToken);

        public async Task<Versioned<ExchangeAccount>?> GetByProviderIdentityAsync(
            UserId userId,
            ExchangeId exchangeId,
            ExchangeAccountProviderIdentity providerIdentity,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.GetByProviderIdentityAsync(
                userId,
                exchangeId,
                providerIdentity,
                cancellationToken);
            await Task.Yield();
            barrier.SignalAndWait(cancellationToken);
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

    /// <summary>
    /// Останавливает disconnect после чтения аккаунта и до чтения credential metadata, чтобы
    /// конкурентный reconnect успел зафиксироваться между этими чтениями.
    /// </summary>
    private sealed class MetadataGatedCredentialStore(IExchangeAccountCredentialStore inner)
        : IExchangeAccountCredentialStore
    {
        private readonly TaskCompletionSource<bool> metadataRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> metadataReleased = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int revokeCalls;

        public Task MetadataRequested => metadataRequested.Task;

        public ExchangeAccountCredentialMetadata? ObservedMetadata { get; private set; }

        public int RevokeCalls => Volatile.Read(ref revokeCalls);

        public void ReleaseMetadata() => metadataReleased.TrySetResult(true);

        public Task<ExchangeAccountCredential?> GetAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            inner.GetAsync(userId, exchangeAccountId, cancellationToken);

        public async Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            metadataRequested.TrySetResult(true);
            await metadataReleased.Task.WaitAsync(cancellationToken);
            ObservedMetadata = await inner.GetMetadataAsync(userId, exchangeAccountId, cancellationToken);
            return ObservedMetadata;
        }

        public Task<ConcurrencyVersion> CreateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ExchangeAccountCredentialSecret secret,
            CancellationToken cancellationToken = default) =>
            inner.CreateAsync(userId, exchangeAccountId, secret, cancellationToken);

        public Task<ConcurrencyVersion> RotateAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            ExchangeAccountCredentialSecret replacement,
            CancellationToken cancellationToken = default) =>
            inner.RotateAsync(userId, exchangeAccountId, expectedVersion, replacement, cancellationToken);

        public Task RevokeAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref revokeCalls);
            return inner.RevokeAsync(userId, exchangeAccountId, expectedVersion, cancellationToken);
        }

        public Task<ConcurrencyVersion> ReprotectAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            inner.ReprotectAsync(userId, exchangeAccountId, expectedVersion, cancellationToken);
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
