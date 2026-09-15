using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class RecommendationPublicationTransaction(
    TradeSystemDbContext dbContext,
    IRecommendationRepository recommendationRepository,
    IRecommendationStabilityStateRepository stabilityStateRepository)
    : IRecommendationPublicationTransaction
{
    public Task PublishInitialAsync(
        UserId userId,
        Recommendation successor,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            successor.PositionId,
            async () =>
            {
                await recommendationRepository.EnsureCurrentAsync(
                    userId,
                    successor.PositionId,
                    expectedCurrent,
                    cancellationToken);
                EnsureExpectedCurrentIsAbsent(expectedCurrent);

                await recommendationRepository.SaveAsync(
                    userId,
                    successor,
                    expectedVersion: null,
                    cancellationToken);
                await stabilityStateRepository.DeleteExpectedAsync(
                    userId,
                    successor.PositionId,
                    expectedPending,
                    cancellationToken);
            },
            cancellationToken);

    public Task ReplaceAsync(
        UserId userId,
        Recommendation current,
        Recommendation successor,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            successor.PositionId,
            async () =>
            {
                await recommendationRepository.EnsureCurrentAsync(
                    userId,
                    successor.PositionId,
                    expectedCurrent,
                    cancellationToken);
                var expectedVersion = GetExpectedCurrentVersion(
                    expectedCurrent,
                    current,
                    successor.PositionId);

                await recommendationRepository.SaveAsync(
                    userId,
                    current,
                    expectedVersion,
                    cancellationToken);
                await recommendationRepository.SaveAsync(
                    userId,
                    successor,
                    expectedVersion: null,
                    cancellationToken);
                await stabilityStateRepository.DeleteExpectedAsync(
                    userId,
                    successor.PositionId,
                    expectedPending,
                    cancellationToken);
            },
            cancellationToken);

    public Task SavePendingAsync(
        UserId userId,
        PositionId positionId,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateSnapshot state,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            positionId,
            async () =>
            {
                await recommendationRepository.EnsureCurrentAsync(
                    userId,
                    positionId,
                    expectedCurrent,
                    cancellationToken);
                await stabilityStateRepository.SaveAsync(
                    userId,
                    positionId,
                    state,
                    expectedPending,
                    cancellationToken);
            },
            cancellationToken);

    public Task ConfirmKeepExistingAsync(
        UserId userId,
        PositionId positionId,
        RecommendationCurrentExpectation expectedCurrent,
        RecommendationStabilityStateExpectation expectedPending,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            positionId,
            async () =>
            {
                await recommendationRepository.EnsureCurrentAsync(
                    userId,
                    positionId,
                    expectedCurrent,
                    cancellationToken);
                await stabilityStateRepository.DeleteExpectedAsync(
                    userId,
                    positionId,
                    expectedPending,
                    cancellationToken);
            },
            cancellationToken);

    private async Task ExecuteAsync(
        UserId userId,
        PositionId positionId,
        Func<Task> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RecommendationPositionLock.LockAsync(
                dbContext,
                userId,
                positionId,
                cancellationToken).ConfigureAwait(false);
            await operation().ConfigureAwait(false);
            return;
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecommendationPositionLock.LockAsync(
                dbContext,
                userId,
                positionId,
                cancellationToken).ConfigureAwait(false);
            await operation().ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
            when (PostgreSqlConcurrencyConflictDetector.IsCurrentRecommendationConflict(exception))
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }

            throw new ConcurrencyConflictException(
                "Current recommendation publication lost a database uniqueness race.",
                exception);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }

            throw;
        }
    }

    private static ConcurrencyVersion GetExpectedCurrentVersion(
        RecommendationCurrentExpectation expectedCurrent,
        Recommendation current,
        PositionId positionId)
    {
        if (expectedCurrent is not RecommendationCurrentExpectation.Present present ||
            present.RecommendationId != current.Id)
        {
            throw new ConcurrencyConflictException(
                $"Current recommendation for position {positionId} changed concurrently.");
        }

        return present.Version;
    }

    private static void EnsureExpectedCurrentIsAbsent(
        RecommendationCurrentExpectation expectedCurrent)
    {
        if (expectedCurrent is not RecommendationCurrentExpectation.Absent)
            throw new ConcurrencyConflictException(
                "Initial recommendation publication requires an absent current expectation.");
    }
}
