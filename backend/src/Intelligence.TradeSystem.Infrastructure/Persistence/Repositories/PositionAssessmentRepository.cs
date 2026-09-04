using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionAssessmentRepository(TradeSystemDbContext dbContext)
    : IPositionAssessmentRepository
{
    public async Task<PositionAssessment?> GetByIdAsync(
        PositionAssessmentId id,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.PositionAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(assessment => assessment.Id == id.Value, cancellationToken);

        if (entity is null) return null;

        var reasons = await dbContext.PositionAssessmentReasons
            .AsNoTracking()
            .Where(reason => reason.PositionAssessmentId == id.Value)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);

        return PositionAssessmentMapper.ToDomain(entity, reasons);
    }

    public async Task SaveAsync(
        PositionAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var mapped = PositionAssessmentMapper.ToEntity(assessment);
        var tracked = dbContext.ChangeTracker.Entries<PositionAssessmentEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        PositionAssessmentEntity? existing;
        if (tracked is not null)
        {
            existing = tracked.Entity;
        }
        else
        {
            existing = await dbContext.PositionAssessments
                .AsNoTracking()
                .SingleOrDefaultAsync(entity => entity.Id == mapped.Id, cancellationToken);
        }

        var persistedReasons = existing is null
            ? []
            : await dbContext.PositionAssessmentReasons
                .AsNoTracking()
                .Where(reason => reason.PositionAssessmentId == mapped.Id)
                .OrderBy(reason => reason.Sequence)
                .ToArrayAsync(cancellationToken);
        if (existing is not null)
            EnsureReasonsMatch(
                persistedReasons.Select(reason => reason.ReasonCode),
                assessment.ReasonCodes,
                assessment.Id);

        if (existing is null)
            dbContext.PositionAssessments.Add(mapped);
        else if (tracked is not null)
            tracked.CurrentValues.SetValues(mapped);
        else
            dbContext.PositionAssessments.Update(mapped);

        if (existing is null)
            dbContext.PositionAssessmentReasons.AddRange(
                PositionAssessmentMapper.ToReasonEntities(assessment));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureReasonsMatch(
        IEnumerable<Domain.Decisions.ReasonCode> persisted,
        IReadOnlyList<Domain.Decisions.ReasonCode> current,
        PositionAssessmentId id)
    {
        if (!persisted.SequenceEqual(current))
            throw new InvalidOperationException(
                $"Position assessment {id} reason codes are immutable and cannot be replaced.");
    }
}
