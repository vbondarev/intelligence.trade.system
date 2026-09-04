using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionRepository(TradeSystemDbContext dbContext) : IPositionRepository
{
    public async Task<Position?> GetByIdAsync(
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

        return PositionMapper.ToDomain(entity, changes);
    }

    public async Task SaveAsync(
        Position position,
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

        var persistedChanges = existing is null
            ? []
            : await dbContext.PositionChanges
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

        if (existing is null)
        {
            dbContext.Positions.Add(mapped);
        }
        else if (tracked is not null)
        {
            PositionMapper.ApplyToEntity(tracked.Entity, position);
        }
        else
        {
            dbContext.Positions.Update(mapped);
        }

        var newChanges = position.Changes
            .Skip(persistedChanges.Length)
            .Select((change, index) => PositionChangeMapper.ToEntity(
                change, persistedChanges.Length + index + 1));
        dbContext.PositionChanges.AddRange(newChanges);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
