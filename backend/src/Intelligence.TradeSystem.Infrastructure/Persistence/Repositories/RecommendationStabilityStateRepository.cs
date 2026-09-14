using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class RecommendationStabilityStateRepository(TradeSystemDbContext dbContext)
    : IRecommendationStabilityStateRepository
{
    public async Task<Versioned<RecommendationStabilityStateSnapshot>?> GetAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);

        var entity = await dbContext.RecommendationStabilityStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                state =>
                    state.PositionId == positionId.Value &&
                    dbContext.Positions.Any(position =>
                        position.Id == state.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);
        if (entity is null)
            return null;

        return new(
            RecommendationStabilityStateMapper.ToDomain(entity),
            new ConcurrencyVersion(entity.Version));
    }

    public Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        PositionId positionId,
        RecommendationStabilityStateSnapshot state,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            userId,
            positionId,
            token => SaveCoreAsync(userId, positionId, state, expectedVersion, token),
            cancellationToken);

    private async Task<ConcurrencyVersion> SaveCoreAsync(
        UserId userId,
        PositionId positionId,
        RecommendationStabilityStateSnapshot state,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);
        ArgumentNullException.ThrowIfNull(state);

        var ownsPosition = await dbContext.Positions.AnyAsync(
            position =>
                position.Id == positionId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == position.ExchangeAccountId &&
                    account.UserId == userId.Value),
            cancellationToken);
        var ownsBaseline = ownsPosition && await dbContext.Recommendations.AnyAsync(
            recommendation =>
                recommendation.Id == state.BaselineRecommendationId.Value &&
                recommendation.PositionId == positionId.Value &&
                (recommendation.Status == RecommendationStatus.Active ||
                 recommendation.Status == RecommendationStatus.Acknowledged) &&
                dbContext.Positions.Any(position =>
                    position.Id == recommendation.PositionId &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value)),
            cancellationToken);
        if (!ownsBaseline)
            throw new ConcurrencyConflictException(
                $"Recommendation stability state for position {positionId} is unavailable in the requested user scope.");

        var mapped = RecommendationStabilityStateMapper.ToEntity(positionId, state);
        var existing = await dbContext.RecommendationStabilityStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                current =>
                    current.PositionId == positionId.Value &&
                    dbContext.Positions.Any(position =>
                        position.Id == current.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)),
                cancellationToken);

        if (expectedVersion is null && existing is not null)
            throw new ConcurrencyConflictException(
                $"Recommendation stability state for position {positionId} already exists.");
        if (expectedVersion is not null && existing is null)
            throw new ConcurrencyConflictException(
                $"Recommendation stability state for position {positionId} was deleted concurrently.");

        if (existing is null)
        {
            var version = ConcurrencyVersion.Initial;
            mapped.Version = version.Value;
            dbContext.RecommendationStabilityStates.Add(mapped);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
                when (PostgreSqlConcurrencyConflictDetector.IsDuplicatePrimaryKey(
                    exception,
                    "PK_recommendation_stability_states"))
            {
                throw new ConcurrencyConflictException(
                    $"Recommendation stability state for position {positionId} was inserted concurrently.",
                    exception);
            }

            return version;
        }

        var newVersion = expectedVersion!.Value.Next();
        var affected = await dbContext.RecommendationStabilityStates
            .Where(
                current =>
                    current.PositionId == positionId.Value &&
                    current.Version == expectedVersion.Value.Value &&
                    dbContext.Positions.Any(position =>
                        position.Id == current.PositionId &&
                        dbContext.ExchangeAccounts.Any(account =>
                            account.Id == position.ExchangeAccountId &&
                            account.UserId == userId.Value)))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(current => current.BaselineRecommendationId, mapped.BaselineRecommendationId)
                    .SetProperty(current => current.SemanticStateJson, mapped.SemanticStateJson)
                    .SetProperty(current => current.FirstObservedAt, mapped.FirstObservedAt)
                    .SetProperty(current => current.LastObservedAt, mapped.LastObservedAt)
                    .SetProperty(current => current.ConsecutiveObservations, mapped.ConsecutiveObservations)
                    .SetProperty(current => current.Version, newVersion.Value),
                cancellationToken);
        if (affected != 1)
            throw new ConcurrencyConflictException(
                $"Recommendation stability state for position {positionId} was changed concurrently.");

        return newVersion;
    }

    public Task DeleteAsync(
        UserId userId,
        PositionId positionId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(
            userId,
            positionId,
            async token =>
            {
                EnsureUserId(userId);
                EnsurePositionId(positionId);

                var affected = await dbContext.RecommendationStabilityStates
                    .Where(
                        state =>
                            state.PositionId == positionId.Value &&
                            state.Version == expectedVersion.Value &&
                            dbContext.Positions.Any(position =>
                                position.Id == state.PositionId &&
                                dbContext.ExchangeAccounts.Any(account =>
                                    account.Id == position.ExchangeAccountId &&
                                    account.UserId == userId.Value)))
                    .ExecuteDeleteAsync(token);
                if (affected != 1)
                    throw new ConcurrencyConflictException(
                        $"Recommendation stability state for position {positionId} is unavailable or changed.");
                return true;
            },
            cancellationToken);

    private async Task<T> ExecuteWriteAsync<T>(
        UserId userId,
        PositionId positionId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);
        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RecommendationPositionLock.LockAsync(
                dbContext,
                userId,
                positionId,
                cancellationToken);
            return await operation(cancellationToken);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await RecommendationPositionLock.LockAsync(
                dbContext,
                userId,
                positionId,
                cancellationToken);
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
}
