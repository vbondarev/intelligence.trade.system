using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class PositionEvaluationTransaction(TradeSystemDbContext dbContext)
    : IPositionEvaluationTransaction
{
    public async Task ExecuteAsync(
        UserId userId,
        PositionId positionId,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await RecommendationPositionLock
                .LockAsync(
                    dbContext,
                    userId,
                    positionId,
                    cancellationToken)
                .ConfigureAwait(false);
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RecommendationPositionLock
                .LockAsync(
                    dbContext,
                    userId,
                    positionId,
                    cancellationToken)
                .ConfigureAwait(false);
            await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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
