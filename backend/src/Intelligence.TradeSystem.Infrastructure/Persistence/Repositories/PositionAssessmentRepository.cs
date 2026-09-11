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
        UserId userId,
        PositionAssessmentId id,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        var entity = await dbContext.PositionAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                assessment =>
                    assessment.Id == id.Value &&
                    dbContext.Positions.Any(position =>
                        position.Id == assessment.PositionId &&
                        position.ExchangeAccountId == assessment.ExchangeAccountId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);

        if (entity is null) return null;

        var reasons = await dbContext.PositionAssessmentReasons
            .AsNoTracking()
            .Where(reason => reason.PositionAssessmentId == id.Value)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);

        return PositionAssessmentMapper.ToDomain(entity, reasons);
    }

    public async Task SaveAsync(
        UserId userId,
        PositionAssessment assessment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var mapped = PositionAssessmentMapper.ToEntity(assessment);
        EnsureUserId(userId);
        if (!await dbContext.Positions.AnyAsync(
                position =>
                    position.Id == mapped.PositionId &&
                    position.ExchangeAccountId == mapped.ExchangeAccountId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value),
                cancellationToken))
        {
            throw new InvalidOperationException(
                "A position assessment can only be saved within its owning user scope.");
        }

        var persisted = await dbContext.PositionAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.Id == mapped.Id &&
                    dbContext.Positions.Any(position =>
                        position.Id == entity.PositionId &&
                        position.ExchangeAccountId == entity.ExchangeAccountId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);

        var tracked = dbContext.ChangeTracker.Entries<PositionAssessmentEntity>()
            .SingleOrDefault(entry => entry.Entity.Id == mapped.Id);

        if (persisted is null && tracked is not null)
        {
            throw new InvalidOperationException(
                "The position assessment is unavailable in the requested user scope.");
        }

        if (persisted is not null &&
            (persisted.PositionId != mapped.PositionId ||
             persisted.ExchangeAccountId != mapped.ExchangeAccountId ||
             !string.Equals(persisted.InstrumentId, mapped.InstrumentId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "A position assessment cannot change its persisted identity.");
        }

        PositionAssessmentEntity? existing = tracked?.Entity ?? persisted;
        if (tracked is not null &&
            persisted is not null &&
            (tracked.Entity.PositionId != persisted.PositionId ||
             tracked.Entity.ExchangeAccountId != persisted.ExchangeAccountId ||
             !string.Equals(
                 tracked.Entity.InstrumentId,
                 persisted.InstrumentId,
                 StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The tracked position assessment does not match its persisted identity.");
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
        if (!persisted
                .OrderBy(reason => (int)reason)
                .SequenceEqual(current.OrderBy(reason => (int)reason)))
            throw new InvalidOperationException(
                $"Position assessment {id} reason codes are immutable and cannot be replaced.");
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }
}
