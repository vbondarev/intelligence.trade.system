using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class ExchangeAccountRepository(TradeSystemDbContext dbContext) : IExchangeAccountRepository
{
    public async Task<Versioned<ExchangeAccount>?> GetByIdAsync(
        ExchangeAccountId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == id.Value, cancellationToken);

        return entity is null
            ? null
            : new Versioned<ExchangeAccount>(
                ExchangeAccountMapper.ToDomain(entity), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        ExchangeAccount account,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var mapped = ExchangeAccountMapper.ToEntity(account);
        var tracked = dbContext.ChangeTracker
            .Entries<ExchangeAccountEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        ExchangeAccountEntity? existing;
        if (tracked is not null)
        {
            existing = tracked.Entity;
        }
        else
        {
            existing = await dbContext.ExchangeAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == mapped.Id, cancellationToken);
        }

        if (expectedVersion is null && existing is not null)
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {account.Id} already exists and cannot be inserted again.");
        if (expectedVersion is not null && existing is null)
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {account.Id} was deleted concurrently and cannot be updated.");

        ConcurrencyVersion newVersion;
        if (existing is null)
        {
            newVersion = ConcurrencyVersion.Initial;
            mapped.Version = newVersion.Value;
            dbContext.ExchangeAccounts.Add(mapped);
        }
        else
        {
            newVersion = expectedVersion!.Value.Next();
            mapped.Version = newVersion.Value;
            if (tracked is not null)
            {
                tracked.CurrentValues.SetValues(mapped);
                tracked.Property(entry => entry.Version).OriginalValue = expectedVersion.Value.Value;
            }
            else
            {
                dbContext.Attach(mapped);
                var entry = dbContext.Entry(mapped);
                entry.Property(e => e.Version).OriginalValue = expectedVersion.Value.Value;
                entry.State = EntityState.Modified;
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {account.Id} was modified or deleted concurrently.", exception);
        }
        catch (DbUpdateException exception)
            when (existing is null &&
                  expectedVersion is null &&
                  PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                      exception,
                      "PK_exchange_accounts"))
        {
            throw new ConcurrencyConflictException(
                $"ExchangeAccount {account.Id} was inserted concurrently.", exception);
        }

        return newVersion;
    }
}
