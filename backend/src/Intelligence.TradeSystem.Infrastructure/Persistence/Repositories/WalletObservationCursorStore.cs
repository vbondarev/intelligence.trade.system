using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal sealed class WalletObservationCursorStore(TradeSystemDbContext dbContext)
    : IWalletObservationCursorStore
{
    public async Task<DateTimeOffset?> GetLastAcceptedMessageCreatedAtAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);

        var value = await OwnedCursors(userId, exchangeAccountId)
            .Select(cursor => (DateTimeOffset?)cursor.LastMessageCreatedAt)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return value;
    }

    public async Task SaveAcceptedAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset messageCreatedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);

        var owned = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .AnyAsync(
                account =>
                    account.Id == exchangeAccountId.Value &&
                    account.UserId == userId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!owned)
        {
            throw new InvalidOperationException(
                "The exchange account is unavailable in the user scope for the wallet observation cursor.");
        }

        var canonical = TimestampCanonicalizer.ToUtcMicroseconds(messageCreatedAt);
        var existing = await dbContext.ExchangeWalletObservationCursors
            .SingleOrDefaultAsync(
                cursor => cursor.ExchangeAccountId == exchangeAccountId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            dbContext.ExchangeWalletObservationCursors.Add(new ExchangeWalletObservationCursorEntity
            {
                ExchangeAccountId = exchangeAccountId.Value,
                LastMessageCreatedAt = canonical,
            });
        }
        else if (existing.LastMessageCreatedAt < canonical)
        {
            // Курсор только продвигается вперёд вместе с принятым капиталом; регресс запрещён.
            existing.LastMessageCreatedAt = canonical;
        }
        else if (existing.LastMessageCreatedAt > canonical)
        {
            throw new InvalidOperationException(
                "The wallet observation cursor cannot regress to an older exchange MessageCreatedAt.");
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ExchangeWalletObservationCursorEntity> OwnedCursors(
        UserId userId,
        ExchangeAccountId exchangeAccountId) =>
        dbContext.ExchangeWalletObservationCursors
            .AsNoTracking()
            .Where(cursor => cursor.ExchangeAccountId == exchangeAccountId.Value)
            .Where(cursor => dbContext.ExchangeAccounts.Any(account =>
                account.Id == cursor.ExchangeAccountId &&
                account.UserId == userId.Value));

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
        {
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
        }
    }

    private static void EnsureAccountId(ExchangeAccountId exchangeAccountId)
    {
        if (exchangeAccountId == default)
        {
            throw new ArgumentException(
                "ExchangeAccountId must be initialized.",
                nameof(exchangeAccountId));
        }
    }
}
