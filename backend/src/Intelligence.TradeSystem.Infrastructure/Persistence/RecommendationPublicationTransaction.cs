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
        ConcurrencyVersion? expectedPendingVersion,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            successor.PositionId,
            async () =>
            {
                await recommendationRepository.SaveAsync(
                    userId,
                    successor,
                    expectedVersion: null,
                    cancellationToken);
                if (expectedPendingVersion is not null)
                {
                    await stabilityStateRepository.DeleteAsync(
                        userId,
                        successor.PositionId,
                        expectedPendingVersion.Value,
                        cancellationToken);
                }
            },
            cancellationToken);

    public Task ReplaceAsync(
        UserId userId,
        Recommendation current,
        ConcurrencyVersion expectedCurrentVersion,
        Recommendation successor,
        ConcurrencyVersion? expectedPendingVersion,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            userId,
            successor.PositionId,
            async () =>
            {
                await recommendationRepository.SaveAsync(
                    userId,
                    current,
                    expectedCurrentVersion,
                    cancellationToken);
                await recommendationRepository.SaveAsync(
                    userId,
                    successor,
                    expectedVersion: null,
                    cancellationToken);
                if (expectedPendingVersion is not null)
                {
                    await stabilityStateRepository.DeleteAsync(
                        userId,
                        successor.PositionId,
                        expectedPendingVersion.Value,
                        cancellationToken);
                }
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
}
