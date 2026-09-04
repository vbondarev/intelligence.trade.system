using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionRepository(TradeSystemDbContext dbContext) : IPositionRepository
{
    public async Task<Versioned<Position>?> GetByIdAsync(
        PositionId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Positions
            .AsNoTracking()
            .SingleOrDefaultAsync(position => position.Id == id.Value, cancellationToken);

        if (entity is null) return null;

        var changes = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change => change.PositionId == id.Value)
            .OrderBy(change => change.Sequence)
            .ToArrayAsync(cancellationToken);

        return new Versioned<Position>(
            PositionMapper.ToDomain(entity, changes), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        Position position,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        var mapped = PositionMapper.ToEntity(position);
        var tracked = dbContext.ChangeTracker.Entries<PositionEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        PositionEntity? existing;
        if (tracked is not null)
        {
            existing = tracked.Entity;
        }
        else
        {
            existing = await dbContext.Positions
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == mapped.Id, cancellationToken);
        }

        if (expectedVersion is null && existing is not null)
            throw new ConcurrencyConflictException(
                $"Position {position.Id} already exists and cannot be inserted again.");
        if (expectedVersion is not null && existing is null)
            throw new ConcurrencyConflictException(
                $"Position {position.Id} was deleted concurrently and cannot be updated.");
        if (expectedVersion is not null &&
            existing is not null &&
            existing.Version != expectedVersion.Value.Value)
            throw new ConcurrencyConflictException(
                $"Position {position.Id} was modified concurrently.");

        if (existing is null)
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
                throw new ConcurrencyConflictException(
                    $"Position {position.Id} was inserted concurrently.", exception);
            }

            return insertVersion;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var newVersion = expectedVersion!.Value.Next();
        mapped.Version = newVersion.Value;
        if (tracked is not null)
        {
            PositionMapper.ApplyToEntity(tracked.Entity, position);
            tracked.Entity.Version = mapped.Version;
            tracked.Property(entry => entry.Version).OriginalValue = expectedVersion.Value.Value;
        }
        else
        {
            dbContext.Positions.Update(mapped);
            var entry = dbContext.Entry(mapped);
            entry.Property(e => e.Version).OriginalValue = expectedVersion.Value.Value;
        }

        // Acquire CAS ownership before reading or staging any new history rows.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ConcurrencyConflictException(
                $"Position {position.Id} was modified or deleted concurrently.", exception);
        }

        var persistedChanges = await dbContext.PositionChanges
            .AsNoTracking()
            .Where(change => change.PositionId == mapped.Id)
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
}
