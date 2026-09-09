using Intelligence.TradeSystem.Application.Accounts;
using Microsoft.EntityFrameworkCore;
using Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

/// <summary>
/// Keeps position, portfolio, account metadata, and outbox writes in one database transaction.
/// </summary>
public sealed class ExchangeAccountSyncTransaction(TradeSystemDbContext dbContext)
    : IExchangeAccountSyncTransaction
{
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await operation(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
            when (PostgreSqlConcurrencyConflictDetector.IsUniqueConstraint(
                exception,
                "ux_positions_active_exchange_key"))
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
            }

            throw new PositionActiveKeyRaceException(exception);
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
