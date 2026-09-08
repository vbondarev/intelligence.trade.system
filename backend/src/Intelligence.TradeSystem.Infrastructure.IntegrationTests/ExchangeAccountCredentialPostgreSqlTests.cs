using System.Data;
using System.Security.Cryptography;
using System.Text;
using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Intelligence.TradeSystem.Infrastructure.IntegrationTests;

[Collection("PostgreSql")]
public sealed class ExchangeAccountCredentialPostgreSqlTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Create_round_trips_after_context_and_protector_restart_without_plaintext_columns()
    {
        var account = CreateAccount();
        const string apiKey = "test-api-key-round-trip";
        const string apiSecret = "test-api-secret-round-trip";
        var keys = CreateKeys("v1");

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            var version = await CreateStore(context, "v1", keys).CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret(apiKey, apiSecret));
            Assert.Equal(ConcurrencyVersion.Initial, version);
        }

        await using var reloadedContext = await CreateMigratedContext();
        var row = await reloadedContext.ExchangeAccountCredentials
            .SingleAsync(credential => credential.ExchangeAccountId == account.Id.Value);
        Assert.False(ContainsSequence(row.Ciphertext, apiKey));
        Assert.False(ContainsSequence(row.Ciphertext, apiSecret));
        Assert.Equal(12, row.Nonce.Length);
        Assert.Equal(16, row.AuthenticationTag.Length);

        var columns = await ReadCredentialColumns(reloadedContext);
        Assert.DoesNotContain("api_key", columns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_secret", columns, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("plaintext", columns, StringComparer.OrdinalIgnoreCase);

        var loaded = await CreateStore(reloadedContext, "v1", keys).GetAsync(
            account.UserId,
            account.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ConcurrencyVersion.Initial, loaded!.Version);
        loaded.Use((loadedApiKey, loadedApiSecret) =>
        {
            Assert.Equal(apiKey, loadedApiKey);
            Assert.Equal(apiSecret, loadedApiSecret);
        });
    }

    [Fact]
    public void Encrypting_the_same_pair_twice_uses_different_nonce_and_ciphertext()
    {
        var account = CreateAccount();
        var protector = CreateProtector("v1", CreateKeys("v1"));
        var secret = new ExchangeAccountCredentialSecret(
            "test-api-key-randomized",
            "test-api-secret-randomized");

        var first = protector.Protect(account.UserId, account.Id, secret);
        var second = protector.Protect(account.UserId, account.Id, secret);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Ciphertext, second.Ciphertext);
        AssertCredentialsEqual(
            secret,
            protector.Unprotect(account.UserId, account.Id, first));
        AssertCredentialsEqual(
            secret,
            protector.Unprotect(account.UserId, account.Id, second));
    }

    [Fact]
    public void Tampering_with_ciphertext_nonce_or_tag_is_rejected_without_plaintext()
    {
        var account = CreateAccount();
        var protector = CreateProtector("v1", CreateKeys("v1"));
        var secret = new ExchangeAccountCredentialSecret(
            "test-api-key-tamper",
            "test-api-secret-tamper");
        var envelope = protector.Protect(account.UserId, account.Id, secret);

        AssertTamperedEnvelopeRejected(
            protector,
            account,
            CloneEnvelope(envelope, ciphertext: Mutate(envelope.Ciphertext)));
        AssertTamperedEnvelopeRejected(
            protector,
            account,
            CloneEnvelope(envelope, nonce: Mutate(envelope.Nonce)));
        AssertTamperedEnvelopeRejected(
            protector,
            account,
            CloneEnvelope(envelope, authenticationTag: Mutate(envelope.AuthenticationTag)));
    }

    [Fact]
    public async Task Metadata_and_revoke_work_when_ciphertext_cannot_be_decrypted()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            await CreateStore(context, "v1", keys).CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-unreadable",
                    "test-api-secret-unreadable"));

            var row = await context.ExchangeAccountCredentials
                .SingleAsync(credential => credential.ExchangeAccountId == account.Id.Value);
            row.AuthenticationTag = Mutate(row.AuthenticationTag);
            await context.SaveChangesAsync();
        }

        await using var readContext = await CreateMigratedContext();
        var store = CreateStore(readContext, "v1", keys);
        var metadata = await store.GetMetadataAsync(account.UserId, account.Id);

        Assert.NotNull(metadata);
        Assert.Equal(ConcurrencyVersion.Initial, metadata!.Version);
        await Assert.ThrowsAsync<CredentialProtectionException>(
            () => store.GetAsync(account.UserId, account.Id));

        await store.RevokeAsync(account.UserId, account.Id, metadata.Version);
        Assert.False(await readContext.ExchangeAccountCredentials
            .AnyAsync(credential => credential.ExchangeAccountId == account.Id.Value));
    }

    [Fact]
    public void Unsupported_payload_format_is_rejected()
    {
        var account = CreateAccount();
        var protector = CreateProtector("v1", CreateKeys("v1"));
        var envelope = protector.Protect(
            account.UserId,
            account.Id,
            new ExchangeAccountCredentialSecret(
                "test-api-key-format",
                "test-api-secret-format"));

        Assert.Throws<CredentialProtectionException>(
            () => protector.Unprotect(
                account.UserId,
                account.Id,
                CloneEnvelope(envelope, formatVersion: 2)));
    }

    [Fact]
    public void Serializer_rejects_an_oversized_api_key_before_encoding_buffer_allocation()
    {
        var secret = new ExchangeAccountCredentialSecret(
            new string('k', CredentialProtectionLimits.MaximumFieldBytes + 1),
            "test-api-secret-oversized-key");

        Assert.Throws<CredentialProtectionException>(
            () => CredentialPayloadSerializer.Serialize(secret));
    }

    [Fact]
    public void Serializer_rejects_an_oversized_api_secret_before_encoding_buffer_allocation()
    {
        var secret = new ExchangeAccountCredentialSecret(
            "test-api-key-oversized-secret",
            new string('s', CredentialProtectionLimits.MaximumFieldBytes + 1));

        Assert.Throws<CredentialProtectionException>(
            () => CredentialPayloadSerializer.Serialize(secret));
    }

    [Fact]
    public void Unprotect_rejects_an_oversized_ciphertext_before_plaintext_allocation()
    {
        var account = CreateAccount();
        var protector = CreateProtector("v1", CreateKeys("v1"));
        var envelope = new ProtectedCredentialEnvelope
        {
            Ciphertext = new byte[CredentialProtectionLimits.MaximumPayloadBytes + 1],
            Nonce = new byte[12],
            AuthenticationTag = new byte[16],
            EncryptionKeyId = "v1",
            FormatVersion = 1,
        };

        Assert.Throws<CredentialProtectionException>(
            () => protector.Unprotect(account.UserId, account.Id, envelope));
    }

    [Fact]
    public void Empty_and_oversized_key_ids_fail_closed()
    {
        var account = CreateAccount();
        var protector = CreateProtector("v1", CreateKeys("v1"));
        var envelope = protector.Protect(
            account.UserId,
            account.Id,
            new ExchangeAccountCredentialSecret(
                "test-api-key-key-id",
                "test-api-secret-key-id"));

        Assert.Throws<CredentialProtectionException>(
            () => protector.Unprotect(
                account.UserId,
                account.Id,
                CloneEnvelope(envelope, encryptionKeyId: string.Empty)));
        Assert.Throws<CredentialProtectionException>(
            () => protector.Unprotect(
                account.UserId,
                account.Id,
                CloneEnvelope(
                    envelope,
                    encryptionKeyId: new string(
                        'x',
                        CredentialProtectionLimits.MaximumKeyIdCharacters + 1))));
    }

    [Fact]
    public async Task Associated_data_prevents_copying_a_row_between_accounts()
    {
        var userId = UserId.New();
        var first = CreateAccount(userId);
        var second = CreateAccount(userId);
        var keys = CreateKeys("v1");

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, first);
            await SaveAccount(context, second);
            var store = CreateStore(context, "v1", keys);
            await store.CreateAsync(
                first.UserId,
                first.Id,
                new ExchangeAccountCredentialSecret("test-api-key-aad", "test-api-secret-aad"));
            await store.CreateAsync(
                second.UserId,
                second.Id,
                new ExchangeAccountCredentialSecret("test-api-key-b", "test-api-secret-b"));

            var firstRow = await context.ExchangeAccountCredentials
                .SingleAsync(row => row.ExchangeAccountId == first.Id.Value);
            var secondRow = await context.ExchangeAccountCredentials
                .SingleAsync(row => row.ExchangeAccountId == second.Id.Value);
            secondRow.Ciphertext = firstRow.Ciphertext.ToArray();
            secondRow.Nonce = firstRow.Nonce.ToArray();
            secondRow.AuthenticationTag = firstRow.AuthenticationTag.ToArray();
            secondRow.EncryptionKeyId = firstRow.EncryptionKeyId;
            secondRow.FormatVersion = firstRow.FormatVersion;
            await context.SaveChangesAsync();
        }

        await using var readContext = await CreateMigratedContext();
        var readStore = CreateStore(readContext, "v1", keys);
        await Assert.ThrowsAsync<CredentialProtectionException>(
            () => readStore.GetAsync(userId, second.Id));
    }

    [Fact]
    public async Task Unknown_key_id_fails_closed_without_fallback()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            await CreateStore(context, "v1", keys).CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret("test-api-key-unknown", "test-api-secret-unknown"));

            var row = await context.ExchangeAccountCredentials
                .SingleAsync(credential => credential.ExchangeAccountId == account.Id.Value);
            row.EncryptionKeyId = "removed-key";
            await context.SaveChangesAsync();
        }

        await using var readContext = await CreateMigratedContext();
        var store = CreateStore(readContext, "v1", keys);
        await Assert.ThrowsAsync<CredentialProtectionException>(
            () => store.GetAsync(account.UserId, account.Id));

        var metadata = await store.GetMetadataAsync(account.UserId, account.Id);
        Assert.NotNull(metadata);
        await store.RevokeAsync(account.UserId, account.Id, metadata!.Version);
        Assert.False(await readContext.ExchangeAccountCredentials
            .AnyAsync(credential => credential.ExchangeAccountId == account.Id.Value));
    }

    [Fact]
    public async Task Rotation_replaces_both_values_and_rejects_stale_writers()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");
        byte[] oldNonce;

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            var store = CreateStore(context, "v1", keys);
            await store.CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret("test-api-key-v1", "test-api-secret-v1"));
            oldNonce = (await context.ExchangeAccountCredentials
                .SingleAsync(row => row.ExchangeAccountId == account.Id.Value)).Nonce;

            var version = await store.RotateAsync(
                account.UserId,
                account.Id,
                ConcurrencyVersion.Initial,
                new ExchangeAccountCredentialSecret("test-api-key-v2", "test-api-secret-v2"));
            Assert.Equal(new ConcurrencyVersion(2), version);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => store.RotateAsync(
                    account.UserId,
                    account.Id,
                    ConcurrencyVersion.Initial,
                    new ExchangeAccountCredentialSecret(
                        "test-api-key-stale",
                        "test-api-secret-stale")));
        }

        await using var readContext = await CreateMigratedContext();
        var rowAfterRotation = await readContext.ExchangeAccountCredentials
            .SingleAsync(row => row.ExchangeAccountId == account.Id.Value);
        Assert.Equal(2L, rowAfterRotation.Version);
        Assert.NotEqual(oldNonce, rowAfterRotation.Nonce);

        var loaded = await CreateStore(readContext, "v1", keys).GetAsync(
            account.UserId,
            account.Id);
        Assert.NotNull(loaded);
        loaded!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("test-api-key-v2", apiKey);
            Assert.Equal("test-api-secret-v2", apiSecret);
        });
    }

    [Fact]
    public async Task Create_and_rotate_use_the_explicit_active_key()
    {
        var account = CreateAccount();
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var keys = CreateKeys(("old", oldKey), ("new", newKey));

        await using var context = await CreateMigratedContext();
        await SaveAccount(context, account);
        var store = CreateStore(context, "new", keys);
        await store.CreateAsync(
            account.UserId,
            account.Id,
            new ExchangeAccountCredentialSecret(
                "test-api-key-active",
                "test-api-secret-active"));

        var createdRow = await context.ExchangeAccountCredentials
            .SingleAsync(row => row.ExchangeAccountId == account.Id.Value);
        Assert.Equal("new", createdRow.EncryptionKeyId);

        await store.RotateAsync(
            account.UserId,
            account.Id,
            ConcurrencyVersion.Initial,
            new ExchangeAccountCredentialSecret(
                "test-api-key-active-rotated",
                "test-api-secret-active-rotated"));

        var rotatedRow = await context.ExchangeAccountCredentials
            .SingleAsync(row => row.ExchangeAccountId == account.Id.Value);
        Assert.Equal("new", rotatedRow.EncryptionKeyId);
    }

    [Fact]
    public async Task Rotate_uses_the_active_key_after_an_old_key_write()
    {
        var account = CreateAccount();
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var oldOnlyKeys = CreateKeys(("old", oldKey));
        var rolloverKeys = CreateKeys(("old", oldKey), ("new", newKey));
        var newOnlyKeys = CreateKeys(("new", newKey));

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            await CreateStore(context, "old", oldOnlyKeys).CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-old",
                    "test-api-secret-old"));
        }

        await using (var context = await CreateMigratedContext())
        {
            await CreateStore(context, "new", rolloverKeys).RotateAsync(
                account.UserId,
                account.Id,
                ConcurrencyVersion.Initial,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-rotated-active",
                    "test-api-secret-rotated-active"));

            var row = await context.ExchangeAccountCredentials
                .SingleAsync(item => item.ExchangeAccountId == account.Id.Value);
            Assert.Equal("new", row.EncryptionKeyId);
        }

        await using var newOnlyContext = await CreateMigratedContext();
        var reloaded = await CreateStore(newOnlyContext, "new", newOnlyKeys)
            .GetAsync(account.UserId, account.Id);
        Assert.NotNull(reloaded);
        reloaded!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("test-api-key-rotated-active", apiKey);
            Assert.Equal("test-api-secret-rotated-active", apiSecret);
        });
    }

    [Fact]
    public async Task Revoke_removes_the_row_and_requires_the_current_version()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");

        await using var context = await CreateMigratedContext();
        await SaveAccount(context, account);
        var store = CreateStore(context, "v1", keys);
        await store.CreateAsync(
            account.UserId,
            account.Id,
            new ExchangeAccountCredentialSecret("test-api-key-revoke", "test-api-secret-revoke"));

        await store.RevokeAsync(account.UserId, account.Id, ConcurrencyVersion.Initial);
        Assert.Null(await store.GetAsync(account.UserId, account.Id));
        Assert.False(await context.ExchangeAccountCredentials
            .AnyAsync(row => row.ExchangeAccountId == account.Id.Value));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.RevokeAsync(account.UserId, account.Id, ConcurrencyVersion.Initial));
    }

    [Fact]
    public async Task Stale_reprotect_cannot_overwrite_rotated_credentials()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            var store = CreateStore(context, "v1", keys);
            await store.CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-before-reprotect",
                    "test-api-secret-before-reprotect"));
            await store.RotateAsync(
                account.UserId,
                account.Id,
                ConcurrencyVersion.Initial,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-after-rotate",
                    "test-api-secret-after-rotate"));

            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => store.ReprotectAsync(
                    account.UserId,
                    account.Id,
                    ConcurrencyVersion.Initial));
        }

        await using var readContext = await CreateMigratedContext();
        var row = await readContext.ExchangeAccountCredentials
            .SingleAsync(item => item.ExchangeAccountId == account.Id.Value);
        Assert.Equal(2L, row.Version);
        var loaded = await CreateStore(readContext, "v1", keys)
            .GetAsync(account.UserId, account.Id);
        Assert.NotNull(loaded);
        loaded!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("test-api-key-after-rotate", apiKey);
            Assert.Equal("test-api-secret-after-rotate", apiSecret);
        });
    }

    [Fact]
    public async Task Reprotect_reads_old_key_and_writes_the_explicit_active_key()
    {
        var account = CreateAccount();
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var oldOnlyKeys = CreateKeys(("old", oldKey));
        var rolloverKeys = CreateKeys(("old", oldKey), ("new", newKey));
        var newOnlyKeys = CreateKeys(("new", newKey));
        byte[] oldNonce;

        await using (var context = await CreateMigratedContext())
        {
            await SaveAccount(context, account);
            var oldStore = CreateStore(context, "old", oldOnlyKeys);
            await oldStore.CreateAsync(
                account.UserId,
                account.Id,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-reprotect",
                    "test-api-secret-reprotect"));
            oldNonce = (await context.ExchangeAccountCredentials
                .SingleAsync(row => row.ExchangeAccountId == account.Id.Value)).Nonce;
        }

        await using (var rolloverContext = await CreateMigratedContext())
        {
            var rolloverStore = CreateStore(rolloverContext, "new", rolloverKeys);
            var loaded = await rolloverStore.GetAsync(account.UserId, account.Id);
            Assert.NotNull(loaded);
            var newVersion = await rolloverStore.ReprotectAsync(
                account.UserId,
                account.Id,
                ConcurrencyVersion.Initial);
            Assert.Equal(new ConcurrencyVersion(2), newVersion);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => rolloverStore.ReprotectAsync(
                    account.UserId,
                    account.Id,
                    ConcurrencyVersion.Initial));

            var row = await rolloverContext.ExchangeAccountCredentials
                .SingleAsync(item => item.ExchangeAccountId == account.Id.Value);
            Assert.Equal("new", row.EncryptionKeyId);
            Assert.Equal(2L, row.Version);
            Assert.NotEqual(oldNonce, row.Nonce);
        }

        await using var newOnlyContext = await CreateMigratedContext();
        var newOnlyStore = CreateStore(newOnlyContext, "new", newOnlyKeys);
        var reloaded = await newOnlyStore.GetAsync(account.UserId, account.Id);
        Assert.NotNull(reloaded);
        reloaded!.Use((apiKey, apiSecret) =>
        {
            Assert.Equal("test-api-key-reprotect", apiKey);
            Assert.Equal("test-api-secret-reprotect", apiSecret);
        });
    }

    [Fact]
    public async Task Foreign_user_cannot_read_or_mutate_credentials()
    {
        var owner = CreateAccount();
        var foreign = CreateAccount();
        var keys = CreateKeys("v1");

        await using var context = await CreateMigratedContext();
        await SaveAccount(context, owner);
        await SaveAccount(context, foreign);
        var store = CreateStore(context, "v1", keys);
        await store.CreateAsync(
            foreign.UserId,
            foreign.Id,
            new ExchangeAccountCredentialSecret("test-api-key-foreign", "test-api-secret-foreign"));

        Assert.Null(await store.GetAsync(owner.UserId, foreign.Id));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.CreateAsync(
                owner.UserId,
                foreign.Id,
                new ExchangeAccountCredentialSecret("test-api-key-write", "test-api-secret-write")));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.RotateAsync(
                owner.UserId,
                foreign.Id,
                ConcurrencyVersion.Initial,
                new ExchangeAccountCredentialSecret(
                    "test-api-key-rotate",
                    "test-api-secret-rotate")));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.ReprotectAsync(
                owner.UserId,
                foreign.Id,
                ConcurrencyVersion.Initial));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => store.RevokeAsync(owner.UserId, foreign.Id, ConcurrencyVersion.Initial));

        var row = await context.ExchangeAccountCredentials
            .SingleAsync(item => item.ExchangeAccountId == foreign.Id.Value);
        Assert.Equal(ConcurrencyVersion.Initial.Value, row.Version);
    }

    [Fact]
    public async Task Deleting_an_account_cascades_to_its_encrypted_credentials()
    {
        var account = CreateAccount();
        var keys = CreateKeys("v1");

        await using var context = await CreateMigratedContext();
        await SaveAccount(context, account);
        await CreateStore(context, "v1", keys).CreateAsync(
            account.UserId,
            account.Id,
            new ExchangeAccountCredentialSecret("test-api-key-cascade", "test-api-secret-cascade"));

        await context.ExchangeAccounts
            .Where(item => item.Id == account.Id.Value)
            .ExecuteDeleteAsync();

        Assert.False(await context.ExchangeAccountCredentials
            .AnyAsync(item => item.ExchangeAccountId == account.Id.Value));
    }

    [Fact]
    public async Task PostgreSql_rejects_an_oversized_ciphertext()
    {
        var account = CreateAccount();

        await using var context = await CreateMigratedContext();
        await SaveAccount(context, account);
        context.ExchangeAccountCredentials.Add(new ExchangeAccountCredentialEntity
        {
            ExchangeAccountId = account.Id.Value,
            Ciphertext = new byte[CredentialProtectionLimits.MaximumPayloadBytes + 1],
            Nonce = new byte[12],
            AuthenticationTag = new byte[16],
            EncryptionKeyId = "v1",
            FormatVersion = 1,
            Version = 1,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private async Task<TradeSystemDbContext> CreateMigratedContext()
    {
        var context = fixture.CreateContext();
        await context.Database.MigrateAsync();
        return context;
    }

    private static async Task SaveAccount(
        TradeSystemDbContext context,
        ExchangeAccount account) =>
        await new ExchangeAccountRepository(context)
            .SaveAsync(account.UserId, account, expectedVersion: null);

    private static ExchangeAccountCredentialStore CreateStore(
        TradeSystemDbContext context,
        string activeKeyId,
        IReadOnlyDictionary<string, string> keys) =>
        new(context, CreateProtector(activeKeyId, keys));

    private static AesGcmExchangeCredentialProtector CreateProtector(
        string activeKeyId,
        IReadOnlyDictionary<string, string> keys) =>
        new(CredentialKeyRing.Create(new CredentialProtectionOptions
        {
            ActiveKeyId = activeKeyId,
            Keys = keys,
        }));

    private static Dictionary<string, string> CreateKeys(params string[] ids) =>
        ids.ToDictionary(
            id => id,
            _ => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            StringComparer.Ordinal);

    private static Dictionary<string, string> CreateKeys(
        params (string Id, byte[] Key)[] keys) =>
        keys.ToDictionary(
            pair => pair.Id,
            pair => Convert.ToBase64String(pair.Key),
            StringComparer.Ordinal);

    private static ExchangeAccount CreateAccount(UserId? userId = null) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId ?? UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static byte[] Mutate(byte[] value)
    {
        var mutated = value.ToArray();
        mutated[0] ^= 0x01;
        return mutated;
    }

    private static void AssertTamperedEnvelopeRejected(
        AesGcmExchangeCredentialProtector protector,
        ExchangeAccount account,
        ProtectedCredentialEnvelope envelope) =>
        Assert.Throws<CredentialProtectionException>(
            () => protector.Unprotect(account.UserId, account.Id, envelope));

    private static ProtectedCredentialEnvelope CloneEnvelope(
        ProtectedCredentialEnvelope source,
        byte[]? ciphertext = null,
        byte[]? nonce = null,
        byte[]? authenticationTag = null,
        short? formatVersion = null,
        string? encryptionKeyId = null) =>
        new()
        {
            Ciphertext = ciphertext ?? source.Ciphertext.ToArray(),
            Nonce = nonce ?? source.Nonce.ToArray(),
            AuthenticationTag = authenticationTag ?? source.AuthenticationTag.ToArray(),
            EncryptionKeyId = encryptionKeyId ?? source.EncryptionKeyId,
            FormatVersion = formatVersion ?? source.FormatVersion,
        };

    private static void AssertCredentialsEqual(
        ExchangeAccountCredentialSecret expected,
        ExchangeAccountCredentialSecret actual) =>
        expected.Use((expectedApiKey, expectedApiSecret) =>
            actual.Use((actualApiKey, actualApiSecret) =>
            {
                Assert.Equal(expectedApiKey, actualApiKey);
                Assert.Equal(expectedApiSecret, actualApiSecret);
            }));

    private static bool ContainsSequence(byte[] value, string candidate) =>
        ContainsSequence(value, Encoding.UTF8.GetBytes(candidate));

    private static bool ContainsSequence(byte[] value, byte[] candidate)
    {
        for (var i = 0; i <= value.Length - candidate.Length; i++)
        {
            if (value.AsSpan(i, candidate.Length).SequenceEqual(candidate))
                return true;
        }

        return false;
    }

    private static async Task<string[]> ReadCredentialColumns(TradeSystemDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'exchange_account_credentials'
            ORDER BY ordinal_position
            """;
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        return columns.ToArray();
    }
}
