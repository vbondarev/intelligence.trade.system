using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class RecommendationRepository(TradeSystemDbContext dbContext) : IRecommendationRepository
{
    public async Task<Recommendation?> GetByIdAsync(
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

        return RecommendationMapper.ToDomain(entity, reasons, assessment);
    }

    public async Task SaveAsync(
        Recommendation recommendation,
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

        if (existing is null)
            dbContext.Recommendations.Add(mapped);
        else if (tracked is not null)
            tracked.CurrentValues.SetValues(mapped);
        else
            dbContext.Recommendations.Update(mapped);

        if (existing is null)
            dbContext.RecommendationReasons.AddRange(
                RecommendationMapper.ToReasonEntities(recommendation));

        await dbContext.SaveChangesAsync(cancellationToken);
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
