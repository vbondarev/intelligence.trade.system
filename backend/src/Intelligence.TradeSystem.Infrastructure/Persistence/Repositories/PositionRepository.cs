using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionRepository(TradeSystemDbContext dbContext) : IPositionRepository
{
    public async Task<Versioned<Position>?> GetByIdAsync(
        UserId userId,
        PositionId id,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);

        var entity = await dbContext.Positions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                position =>
                    position.Id == id.Value &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value),
                cancellationToken);

        if (entity is null) return null;

        var changes = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change =>
                change.PositionId == id.Value &&
                dbContext.Positions.Any(position =>
                    position.Id == change.PositionId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value)))
            .OrderBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);

        return new Versioned<Position>(
            PositionMapper.ToDomain(entity, changes), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        Position position,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        EnsureUserId(userId);

        var mapped = PositionMapper.ToEntity(position);
        var accountIsOwned = await dbContext.ExchangeAccounts.AnyAsync(
            account => account.Id == mapped.ExchangeAccountId && account.UserId == userId.Value,
            cancellationToken);
        if (!accountIsOwned) throw UnavailablePositionConflict(position.Id);

        if (expectedVersion is null)
        {
            await using var insertTransaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var insertVersion = ConcurrencyVersion.Initial;
            mapped.Version = insertVersion.Value;
            dbContext.Positions.Add(mapped);

            dbContext.PositionChanges.AddRange(
                position.Changes.Select((change, index) => PositionChangeMapper.ToEntity(change, index + 1)));
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                await insertTransaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                    exception,
                    "PK_positions"))
            {
                await insertTransaction.RollbackAsync(CancellationToken.None);
                throw UnavailablePositionConflict(position.Id, exception);
            }

            return insertVersion;
        }

        var ownedPositionId = await dbContext.Positions
            .AsNoTracking()
            .Where(entity =>
                entity.Id == mapped.Id &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == entity.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .Select(entity => (Guid?)entity.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownedPositionId is null)
            throw UnavailablePositionConflict(position.Id);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var newVersion = expectedVersion.Value.Next();

        // Acquire the ownership and version CAS before reading or staging history rows.
        var affected = await dbContext.Positions
            .Where(entity =>
                entity.Id == mapped.Id &&
                entity.ExchangeAccountId == mapped.ExchangeAccountId &&
                entity.Version == expectedVersion.Value.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == entity.ExchangeAccountId &&
                    account.UserId == userId.Value))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.MarketCategory, mapped.MarketCategory)
                    .SetProperty(entity => entity.Size, mapped.Size)
                    .SetProperty(entity => entity.AverageEntryPrice, mapped.AverageEntryPrice)
                    .SetProperty(entity => entity.PositionValue, mapped.PositionValue)
                    .SetProperty(entity => entity.Leverage, mapped.Leverage)
                    .SetProperty(entity => entity.MarkPrice, mapped.MarkPrice)
                    .SetProperty(entity => entity.BreakEvenPrice, mapped.BreakEvenPrice)
                    .SetProperty(entity => entity.LiquidationPrice, mapped.LiquidationPrice)
                    .SetProperty(entity => entity.UnrealizedPnl, mapped.UnrealizedPnl)
                    .SetProperty(entity => entity.TakeProfit, mapped.TakeProfit)
                    .SetProperty(entity => entity.StopLoss, mapped.StopLoss)
                    .SetProperty(entity => entity.TrailingStop, mapped.TrailingStop)
                    .SetProperty(entity => entity.FirstDetectedAt, mapped.FirstDetectedAt)
                    .SetProperty(entity => entity.LastObservedAt, mapped.LastObservedAt)
                    .SetProperty(entity => entity.ClosedAt, mapped.ClosedAt)
                    .SetProperty(entity => entity.TrackingState, mapped.TrackingState)
                    .SetProperty(entity => entity.Version, newVersion.Value),
                cancellationToken);

        if (affected != 1)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw UnavailablePositionConflict(position.Id);
        }

        var persistedChanges = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change =>
                change.PositionId == mapped.Id &&
                dbContext.Positions.Any(entity =>
                    entity.Id == change.PositionId &&
                    entity.ExchangeAccountId == mapped.ExchangeAccountId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == entity.ExchangeAccountId &&
                        account.UserId == userId.Value)))
            .OrderBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);

        if (persistedChanges.Length > position.Changes.Count)
            throw new InvalidOperationException(
                $"Position {position.Id} contains fewer history entries than the database.");

        for (var index = 0; index < persistedChanges.Length; index++)
        {
            if (!PositionChangeMapper.IsEquivalent(
                    persistedChanges[index], position.Changes[index], index + 1))
                throw new InvalidOperationException(
                    $"Position {position.Id} history is not an append-only continuation.");
        }

        var newChanges = position.Changes
            .Skip(persistedChanges.Length)
            .Select((change, index) => PositionChangeMapper.ToEntity(
                change, persistedChanges.Length + index + 1))
            .ToArray();

        if (newChanges.Length > 0)
        {
            dbContext.PositionChanges.AddRange(newChanges);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newVersion;
    }

    private static ConcurrencyConflictException UnavailablePositionConflict(
        PositionId id,
        Exception? innerException = null) =>
        innerException is null
            ? new($"Position {id} is unavailable in the requested user scope.")
            : new($"Position {id} is unavailable in the requested user scope.", innerException);

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
    }
}
