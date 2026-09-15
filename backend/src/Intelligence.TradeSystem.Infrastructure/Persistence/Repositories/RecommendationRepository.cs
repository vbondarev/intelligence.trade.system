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

        return await LoadVersionedAsync(userId, entity, cancellationToken);
    }

    public async Task<Versioned<Recommendation>?> GetCurrentForPositionAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);

        var entities = await dbContext.Recommendations
            .AsNoTracking()
            .Where(
                recommendation =>
                    recommendation.PositionId == positionId.Value &&
                (recommendation.Status == RecommendationStatus.Active ||
                 recommendation.Status == RecommendationStatus.Acknowledged) &&
                    dbContext.Positions.Any(position =>
                        position.Id == recommendation.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)))
            .ToArrayAsync(cancellationToken);
        if (entities.Length > 1)
            throw new InvalidOperationException(
                $"Position {positionId} has multiple current recommendations.");
        if (entities.Length == 0)
            return null;

        return await LoadVersionedAsync(userId, entities[0], cancellationToken);
    }

    public async Task EnsureCurrentAsync(
        UserId userId,
        PositionId positionId,
        RecommendationCurrentExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);
        ArgumentNullException.ThrowIfNull(expectation);

        var currentQuery = dbContext.Recommendations
            .AsNoTracking()
            .Where(
                recommendation =>
                    recommendation.PositionId == positionId.Value &&
                    (recommendation.Status == RecommendationStatus.Active ||
                     recommendation.Status == RecommendationStatus.Acknowledged) &&
                    dbContext.Positions.Any(position =>
                        position.Id == recommendation.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)));

        if (expectation is RecommendationCurrentExpectation.Absent)
        {
            if (await currentQuery.AnyAsync(cancellationToken))
                throw new ConcurrencyConflictException(
                    $"Position {positionId} has a current recommendation unexpectedly.");
            return;
        }

        var present = (RecommendationCurrentExpectation.Present)expectation;
        await RecommendationRecommendationLock.LockAsync(
            dbContext,
            userId,
            positionId,
            present.RecommendationId,
            cancellationToken);
        var current = await currentQuery
            .SingleOrDefaultAsync(
                recommendation => recommendation.Id == present.RecommendationId.Value,
                cancellationToken);
        if (current is null || current.Version != present.Version.Value)
            throw new ConcurrencyConflictException(
                $"Current recommendation for position {positionId} changed concurrently.");
    }

    private async Task<Versioned<Recommendation>> LoadVersionedAsync(
        UserId userId,
        RecommendationEntity entity,
        CancellationToken cancellationToken)
    {
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
                $"Recommendation {entity.Id} references missing assessment {entity.AssessmentId}.");

        var assessmentReasons = await dbContext.PositionAssessmentReasons
            .AsNoTracking()
            .Where(reason => reason.PositionAssessmentId == assessmentEntity.Id)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);
        var assessment = PositionAssessmentMapper.ToDomain(assessmentEntity, assessmentReasons);
        var reasons = await dbContext.RecommendationReasons
            .AsNoTracking()
            .Where(reason => reason.RecommendationId == entity.Id)
            .OrderBy(reason => reason.Sequence)
            .ToArrayAsync(cancellationToken);

        return new Versioned<Recommendation>(
            RecommendationMapper.ToDomain(entity, reasons, assessment), new ConcurrencyVersion(entity.Version));
    }

    public Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        Recommendation recommendation,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            userId,
            recommendation,
            token => SaveCoreAsync(userId, recommendation, expectedVersion, token),
            cancellationToken);

    private async Task<ConcurrencyVersion> SaveCoreAsync(
        UserId userId,
        Recommendation recommendation,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken)
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
                    "PK_recommendations") ||
                    PostgreSqlConcurrencyConflictDetector.IsCurrentRecommendationConflict(exception))
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

    private async Task<T> ExecuteWriteAsync<T>(
        UserId userId,
        Recommendation recommendation,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(recommendation);
        EnsureUserId(userId);
        EnsurePositionId(recommendation.PositionId);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await LockForWriteAsync(userId, recommendation, cancellationToken);
            return await operation(cancellationToken);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await LockForWriteAsync(userId, recommendation, cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }

    private Task LockForWriteAsync(
        UserId userId,
        Recommendation recommendation,
        CancellationToken cancellationToken) =>
        LockForWriteCoreAsync(userId, recommendation, cancellationToken);

    private async Task LockForWriteCoreAsync(
        UserId userId,
        Recommendation recommendation,
        CancellationToken cancellationToken)
    {
        await RecommendationPositionLock.LockAsync(
            dbContext,
            userId,
            recommendation.PositionId,
            cancellationToken);
        await RecommendationRecommendationLock.LockAsync(
            dbContext,
            userId,
            recommendation.PositionId,
            recommendation.Id,
            cancellationToken);
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

    private static void EnsurePositionId(PositionId positionId)
    {
        if (positionId == default)
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
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
            persisted.NextEvaluationAt != current.NextEvaluationAt ||
            !RecommendationMapper.ContinuationContextsEqual(
                persisted.ContinuationContextJson,
                current.ContinuationContextJson) ||
            persisted.CreatedAt != current.CreatedAt ||
            persisted.ValidUntil != current.ValidUntil)
            throw new InvalidOperationException(
                $"Recommendation {id} decision fields are immutable and cannot be replaced.");
    }
}
