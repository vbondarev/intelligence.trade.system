using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class ExchangeAccountRepository(TradeSystemDbContext dbContext) : IExchangeAccountRepository
{
    public async Task<Versioned<ExchangeAccount>?> GetByIdAsync(
        UserId userId,
        ExchangeAccountId id,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        var entity = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                account => account.Id == id.Value && account.UserId == userId.Value,
                cancellationToken);

        return entity is null
            ? null
            : new Versioned<ExchangeAccount>(
                ExchangeAccountMapper.ToDomain(entity), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        ExchangeAccount account,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        EnsureUserId(userId);
        if (account.UserId != userId)
        {
            throw new InvalidOperationException(
                "An exchange account can only be saved within its owning user scope.");
        }

        var mapped = ExchangeAccountMapper.ToEntity(account);

        if (expectedVersion is null)
        {
            if (await dbContext.ExchangeAccounts.AnyAsync(
                    entity => entity.Id == mapped.Id && entity.UserId == userId.Value,
                    cancellationToken))
            {
                throw new ConcurrencyConflictException(
                    $"ExchangeAccount {account.Id} already exists and cannot be inserted again.");
            }

            mapped.Version = ConcurrencyVersion.Initial.Value;
            dbContext.ExchangeAccounts.Add(mapped);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                    exception,
                    "PK_exchange_accounts"))
            {
                throw new ConcurrencyConflictException(
                    $"ExchangeAccount {account.Id} was inserted concurrently.", exception);
            }

            return ConcurrencyVersion.Initial;
        }

        var newVersion = expectedVersion.Value.Next();
        var affected = await dbContext.ExchangeAccounts
            .Where(entity =>
                entity.Id == mapped.Id &&
                entity.UserId == userId.Value &&
                entity.Version == expectedVersion.Value.Value)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.ExchangeId, mapped.ExchangeId)
                    .SetProperty(entity => entity.ConnectionStatus, mapped.ConnectionStatus)
                    .SetProperty(entity => entity.Capabilities, mapped.Capabilities)
                    .SetProperty(entity => entity.LastSyncedAt, mapped.LastSyncedAt)
                    .SetProperty(entity => entity.LastError, mapped.LastError)
                    .SetProperty(
                        entity => entity.LastAppliedBalanceObservationAt,
                        mapped.LastAppliedBalanceObservationAt)
                    .SetProperty(
                        entity => entity.LastAppliedPositionsObservationAt,
                        mapped.LastAppliedPositionsObservationAt)
                    .SetProperty(entity => entity.Version, newVersion.Value),
                cancellationToken);

        if (affected != 1)
        {
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {account.Id} was modified or deleted concurrently.");
        }

        return newVersion;
    }

    public async Task DeleteAsync(
        UserId userId,
        ExchangeAccountId id,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var affected = await dbContext.ExchangeAccounts
            .Where(entity =>
                entity.Id == id.Value &&
                entity.UserId == userId.Value &&
                entity.Version == expectedVersion.Value)
            .ExecuteDeleteAsync(cancellationToken);

        if (affected != 1)
        {
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {id} was modified or deleted concurrently.");
        }
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }
}
