using Intelligence.TradeSystem.Application.Accounts.Credentials;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal sealed class ExchangeAccountCredentialStore(
    TradeSystemDbContext dbContext,
    IExchangeCredentialProtector protector)
    : IExchangeAccountCredentialStore
{
    public async Task<ExchangeAccountCredential?> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);

        var entity = await GetOwnedRowAsync(userId, exchangeAccountId, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var secret = protector.Unprotect(
            userId,
            exchangeAccountId,
            ToEnvelope(entity));
        return new ExchangeAccountCredential(
            secret,
            new ConcurrencyVersion(entity.Version));
    }

    public async Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);

        var version = await GetOwnedVersionAsync(
            userId,
            exchangeAccountId,
            cancellationToken);
        return version is null
            ? null
            : new ExchangeAccountCredentialMetadata(new ConcurrencyVersion(version.Value));
    }

    public async Task<ConcurrencyVersion> CreateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret secret,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);
        ArgumentNullException.ThrowIfNull(secret);

        var envelope = protector.Protect(userId, exchangeAccountId, secret);
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO exchange_account_credentials (
                exchange_account_id,
                ciphertext,
                nonce,
                authentication_tag,
                encryption_key_id,
                format_version,
                version,
                updated_at)
            SELECT
                {exchangeAccountId.Value},
                {envelope.Ciphertext},
                {envelope.Nonce},
                {envelope.AuthenticationTag},
                {envelope.EncryptionKeyId},
                {envelope.FormatVersion},
                {ConcurrencyVersion.Initial.Value},
                {DateTimeOffset.UtcNow}
            FROM exchange_accounts AS account
            WHERE account.exchange_account_id = {exchangeAccountId.Value}
              AND account.user_id = {userId.Value}
            ON CONFLICT (exchange_account_id) DO NOTHING;
            """,
            cancellationToken);

        if (affected != 1)
        {
            throw CredentialWriteConflict();
        }

        return ConcurrencyVersion.Initial;
    }

    public async Task<ConcurrencyVersion> RotateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);
        ArgumentNullException.ThrowIfNull(replacement);

        var current = await GetOwnedRowAsync(userId, exchangeAccountId, cancellationToken);
        if (current is null)
        {
            throw CredentialWriteConflict();
        }

        if (current.Version != expectedVersion.Value)
        {
            throw CredentialWriteConflict();
        }

        var envelope = protector.Protect(userId, exchangeAccountId, replacement);
        var nextVersion = expectedVersion.Next();
        var affected = await UpdateOwnedRowAsync(
            userId,
            exchangeAccountId,
            expectedVersion,
            nextVersion,
            envelope,
            cancellationToken);

        if (affected != 1)
        {
            throw CredentialWriteConflict();
        }

        return nextVersion;
    }

    public async Task RevokeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);

        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM exchange_account_credentials AS credential
            USING exchange_accounts AS account
            WHERE credential.exchange_account_id = {exchangeAccountId.Value}
              AND credential.exchange_account_id = account.exchange_account_id
              AND account.user_id = {userId.Value}
              AND credential.version = {expectedVersion.Value};
            """,
            cancellationToken);

        if (affected != 1)
        {
            throw CredentialWriteConflict();
        }
    }

    public async Task<ConcurrencyVersion> ReprotectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(userId, exchangeAccountId);

        var current = await GetOwnedRowAsync(userId, exchangeAccountId, cancellationToken);
        if (current is null)
        {
            throw CredentialWriteConflict();
        }

        if (current.Version != expectedVersion.Value)
        {
            throw CredentialWriteConflict();
        }

        var currentSecret = protector.Unprotect(
            userId,
            exchangeAccountId,
            ToEnvelope(current));
        var envelope = protector.Protect(userId, exchangeAccountId, currentSecret);
        var nextVersion = expectedVersion.Next();
        var affected = await UpdateOwnedRowAsync(
            userId,
            exchangeAccountId,
            expectedVersion,
            nextVersion,
            envelope,
            cancellationToken);

        if (affected != 1)
        {
            throw CredentialWriteConflict();
        }

        return nextVersion;
    }

    private async Task<ExchangeAccountCredentialEntity?> GetOwnedRowAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken)
    {
        return await (
            from credential in dbContext.ExchangeAccountCredentials.AsNoTracking()
            join account in dbContext.ExchangeAccounts.AsNoTracking()
                on credential.ExchangeAccountId equals account.Id
            where credential.ExchangeAccountId == exchangeAccountId.Value
                  && account.UserId == userId.Value
            select credential)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<long?> GetOwnedVersionAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken)
    {
        return await (
            from credential in dbContext.ExchangeAccountCredentials.AsNoTracking()
            join account in dbContext.ExchangeAccounts.AsNoTracking()
                on credential.ExchangeAccountId equals account.Id
            where credential.ExchangeAccountId == exchangeAccountId.Value
                  && account.UserId == userId.Value
            select (long?)credential.Version)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<int> UpdateOwnedRowAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        ConcurrencyVersion nextVersion,
        ProtectedCredentialEnvelope envelope,
        CancellationToken cancellationToken)
    {
        return await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE exchange_account_credentials AS credential
            SET
                ciphertext = {envelope.Ciphertext},
                nonce = {envelope.Nonce},
                authentication_tag = {envelope.AuthenticationTag},
                encryption_key_id = {envelope.EncryptionKeyId},
                format_version = {envelope.FormatVersion},
                version = {nextVersion.Value},
                updated_at = {DateTimeOffset.UtcNow}
            FROM exchange_accounts AS account
            WHERE credential.exchange_account_id = {exchangeAccountId.Value}
              AND credential.exchange_account_id = account.exchange_account_id
              AND account.user_id = {userId.Value}
              AND credential.version = {expectedVersion.Value};
            """,
            cancellationToken);
    }

    private static ProtectedCredentialEnvelope ToEnvelope(
        ExchangeAccountCredentialEntity entity) =>
        new()
        {
            Ciphertext = entity.Ciphertext,
            Nonce = entity.Nonce,
            AuthenticationTag = entity.AuthenticationTag,
            EncryptionKeyId = entity.EncryptionKeyId,
            FormatVersion = entity.FormatVersion,
        };

    private static ConcurrencyConflictException CredentialWriteConflict() =>
        new("The exchange account credentials are unavailable or were modified concurrently.");

    private static void EnsureIdentity(UserId userId, ExchangeAccountId exchangeAccountId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        if (exchangeAccountId == default)
        {
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
        }
    }
}
