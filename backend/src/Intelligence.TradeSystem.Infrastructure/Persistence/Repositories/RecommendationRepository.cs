using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class RecommendationRepository(TradeSystemDbContext dbContext) : IRecommendationRepository
{
    public async Task<Versioned<Recommendation>?> GetByIdAsync(
        RecommendationId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Recommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(recommendation => recommendation.Id == id.Value, cancellationToken);

        if (entity is null) return null;

        var assessmentEntity = await dbContext.PositionAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == entity.AssessmentId,
                cancellationToken);
        if (assessmentEntity is null)
            throw new InvalidOperationException(
                $"Recommendation {id} references missing assessment {entity.AssessmentId}.");

        var assessmentReasons = await dbContext.PositionAssessmentReasons
            .AsNoTracking()
            .Where(reason => reason.PositionAssessmentId == assessmentEntity.Id)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);
        var assessment = PositionAssessmentMapper.ToDomain(assessmentEntity, assessmentReasons);
        var reasons = await dbContext.RecommendationReasons
            .AsNoTracking()
            .Where(reason => reason.RecommendationId == id.Value)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);

        return new Versioned<Recommendation>(
            RecommendationMapper.ToDomain(entity, reasons, assessment), new ConcurrencyVersion(entity.Version));
    }

    public async Task<ConcurrencyVersion> SaveAsync(
        Recommendation recommendation,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        var mapped = RecommendationMapper.ToEntity(recommendation);
        var tracked = dbContext.ChangeTracker.Entries<RecommendationEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        RecommendationEntity? existing;
        if (tracked is not null)
        {
            existing = tracked.Entity;
        }
        else
        {
            existing = await dbContext.Recommendations
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == mapped.Id, cancellationToken);
        }

        if (expectedVersion is null && existing is not null)
            throw new ConcurrencyConflictException(
                $"Recommendation {recommendation.Id} already exists and cannot be inserted again.");
        if (expectedVersion is not null && existing is null)
            throw new ConcurrencyConflictException(
                $"Recommendation {recommendation.Id} was deleted concurrently and cannot be updated.");

        var persistedReasons = existing is null
            ? []
            : await dbContext.RecommendationReasons
                .AsNoTracking()
                .Where(reason => reason.RecommendationId == mapped.Id)
                .OrderBy(reason => reason.Sequence)
                .ToArrayAsync(cancellationToken);
        if (existing is not null)
            EnsureReasonsMatch(
                persistedReasons.Select(reason => reason.ReasonCode),
                recommendation.ReasonCodes,
                recommendation.Id);

        ConcurrencyVersion newVersion;
        if (existing is null)
        {
            newVersion = ConcurrencyVersion.Initial;
            mapped.Version = newVersion.Value;
            dbContext.Recommendations.Add(mapped);
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
                dbContext.Recommendations.Update(mapped);
                var entry = dbContext.Entry(mapped);
                entry.Property(e => e.Version).OriginalValue = expectedVersion.Value.Value;
            }
        }

        if (existing is null)
            dbContext.RecommendationReasons.AddRange(
                RecommendationMapper.ToReasonEntities(recommendation));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                $"Recommendation {recommendation.Id} was modified or deleted concurrently.", exception);
        }

        return newVersion;
    }

    private static void EnsureReasonsMatch(
        IEnumerable<Domain.Decisions.ReasonCode> persisted,
        IReadOnlyList<Domain.Decisions.ReasonCode> current,
        RecommendationId id)
    {
        if (!persisted.SequenceEqual(current))
            throw new InvalidOperationException(
                $"Recommendation {id} reason codes are immutable and cannot be replaced.");
    }
}
