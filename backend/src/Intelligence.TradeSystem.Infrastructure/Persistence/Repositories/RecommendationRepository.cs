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
        UserId userId,
        RecommendationId id,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        var entity = await dbContext.Recommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                recommendation =>
                    recommendation.Id == id.Value &&
                    dbContext.Positions.Any(position =>
                        position.Id == recommendation.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);

        if (entity is null) return null;

        var assessmentEntity = await dbContext.PositionAssessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == entity.AssessmentId &&
                    candidate.PositionId == entity.PositionId &&
                    dbContext.Positions.Any(position =>
                        position.Id == candidate.PositionId &&
                        position.ExchangeAccountId == candidate.ExchangeAccountId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
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
        UserId userId,
        Recommendation recommendation,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        EnsureUserId(userId);
        var mapped = RecommendationMapper.ToEntity(recommendation);
        var ownsPosition = await dbContext.Positions.AnyAsync(
            position =>
                position.Id == mapped.PositionId &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value),
            cancellationToken);
        var ownsAssessment = ownsPosition && await dbContext.PositionAssessments.AnyAsync(
            assessment =>
                assessment.Id == mapped.AssessmentId &&
                assessment.PositionId == mapped.PositionId &&
                dbContext.Positions.Any(position =>
                    position.Id == assessment.PositionId &&
                    position.ExchangeAccountId == assessment.ExchangeAccountId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value)),
            cancellationToken);
        if (!ownsAssessment) throw UnavailableRecommendationConflict(recommendation.Id);

        var existing = await dbContext.Recommendations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity =>
                    entity.Id == mapped.Id &&
                    entity.PositionId == mapped.PositionId &&
                    entity.AssessmentId == mapped.AssessmentId &&
                    dbContext.Positions.Any(position =>
                        position.Id == entity.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);

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
        {
            EnsureReasonsMatch(
                persistedReasons.Select(reason => reason.ReasonCode),
                recommendation.ReasonCodes,
                recommendation.Id);
            EnsureDecisionMatches(existing, mapped, recommendation.Id);
        }

        if (existing is null)
        {
            var insertVersion = ConcurrencyVersion.Initial;
            mapped.Version = insertVersion.Value;
            dbContext.Recommendations.Add(mapped);
            dbContext.RecommendationReasons.AddRange(
                RecommendationMapper.ToReasonEntities(recommendation));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                    exception,
                    "PK_recommendations"))
            {
                throw UnavailableRecommendationConflict(recommendation.Id, exception);
            }

            return insertVersion;
        }

        var newVersion = expectedVersion!.Value.Next();
        var affected = await dbContext.Recommendations
            .Where(entity =>
                entity.Id == mapped.Id &&
                entity.PositionId == mapped.PositionId &&
                entity.AssessmentId == mapped.AssessmentId &&
                entity.Version == expectedVersion.Value.Value &&
                dbContext.Positions.Any(position =>
                    position.Id == entity.PositionId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(entity => entity.RecommendedAction, mapped.RecommendedAction)
                    .SetProperty(entity => entity.AddDecision, mapped.AddDecision)
                    .SetProperty(entity => entity.PolicyVersion, mapped.PolicyVersion)
                    .SetProperty(entity => entity.CreatedAt, mapped.CreatedAt)
                    .SetProperty(entity => entity.ValidUntil, mapped.ValidUntil)
                    .SetProperty(entity => entity.Status, mapped.Status)
                    .SetProperty(entity => entity.AcknowledgedAt, mapped.AcknowledgedAt)
                    .SetProperty(entity => entity.DismissedAt, mapped.DismissedAt)
                    .SetProperty(entity => entity.SupersededAt, mapped.SupersededAt)
                    .SetProperty(entity => entity.ExpiredAt, mapped.ExpiredAt)
                    .SetProperty(entity => entity.SupersededByRecommendationId, mapped.SupersededByRecommendationId)
                    .SetProperty(entity => entity.Version, newVersion.Value),
                cancellationToken);
        if (affected != 1) throw UnavailableRecommendationConflict(recommendation.Id);

        return newVersion;
    }

    private static ConcurrencyConflictException UnavailableRecommendationConflict(
        RecommendationId id,
        Exception? innerException = null) =>
        innerException is null
            ? new($"Recommendation {id} is unavailable in the requested user scope.")
            : new($"Recommendation {id} is unavailable in the requested user scope.", innerException);

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
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

    private static void EnsureDecisionMatches(
        RecommendationEntity persisted,
        RecommendationEntity current,
        RecommendationId id)
    {
        if (persisted.RecommendedAction != current.RecommendedAction ||
            persisted.AddDecision != current.AddDecision ||
            !string.Equals(persisted.PolicyVersion, current.PolicyVersion, StringComparison.Ordinal) ||
            !string.Equals(persisted.PolicyHash, current.PolicyHash, StringComparison.Ordinal) ||
            persisted.Confidence != current.Confidence ||
            persisted.Priority != current.Priority ||
            !RecommendationMapper.DecisionContextsEqual(
                persisted.DecisionContextJson,
                current.DecisionContextJson) ||
            persisted.CreatedAt != current.CreatedAt ||
            persisted.ValidUntil != current.ValidUntil)
            throw new InvalidOperationException(
                $"Recommendation {id} decision fields are immutable and cannot be replaced.");
    }
}
