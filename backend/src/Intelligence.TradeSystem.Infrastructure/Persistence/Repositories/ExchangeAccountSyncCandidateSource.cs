using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class ExchangeAccountSyncCandidateSource(TradeSystemDbContext dbContext)
    : IExchangeAccountSyncCandidateSource
{
    public async Task<IReadOnlyList<ExchangeAccountSyncCandidate>> GetBatchAsync(
        ExchangeAccountId? after,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, 0);

        var query = dbContext.ExchangeAccounts
            .AsNoTracking()
            .Where(account =>
                account.ExchangeId == ExchangeId.Bybit &&
                (account.ConnectionStatus == ExchangeAccountConnectionStatus.Connected ||
                 account.ConnectionStatus == ExchangeAccountConnectionStatus.Unavailable));

        if (after is { } cursor)
        {
            query = query.Where(account => account.Id.CompareTo(cursor.Value) > 0);
        }

        var rows = await query
            .OrderBy(account => account.Id)
            .Select(account => new
            {
                account.Id,
                account.UserId,
                account.LastSyncedAt,
            })
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new ExchangeAccountSyncCandidate(
                UserId.FromGuid(row.UserId),
                ExchangeAccountId.FromGuid(row.Id),
                row.LastSyncedAt))
            .ToArray();
    }
}
