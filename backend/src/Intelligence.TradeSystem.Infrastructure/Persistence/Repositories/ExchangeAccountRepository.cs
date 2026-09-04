using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class ExchangeAccountRepository(TradeSystemDbContext dbContext) : IExchangeAccountRepository
{
    public async Task<ExchangeAccount?> GetByIdAsync(
        ExchangeAccountId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(account => account.Id == id.Value, cancellationToken);

        return entity is null ? null : ExchangeAccountMapper.ToDomain(entity);
    }

    public async Task SaveAsync(
        ExchangeAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var mapped = ExchangeAccountMapper.ToEntity(account);
        var tracked = dbContext.ChangeTracker
            .Entries<ExchangeAccountEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        if (tracked is not null)
        {
            tracked.CurrentValues.SetValues(mapped);
        }
        else
        {
            var existing = await dbContext.ExchangeAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == mapped.Id, cancellationToken);

            if (existing is null)
            {
                dbContext.ExchangeAccounts.Add(mapped);
            }
            else
            {
                dbContext.ExchangeAccounts.Update(mapped);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
