using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

internal sealed class ExchangeRestOrderingEvidenceStore(TradeSystemDbContext dbContext)
    : IExchangeRestOrderingEvidenceStore
{
    public async Task AppendAsync(
        UserId userId,
        ExchangeRestOrderingInterval interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interval);
        EnsureUserId(userId);
        ValidateInterval(interval);

        // Строка аккаунта подтверждает user-scope и участвует в порядке блокировок каскадного удаления.
        var owned = await dbContext.ExchangeAccounts
            .AsNoTracking()
            .AnyAsync(
                account =>
                    account.Id == interval.ExchangeAccountId.Value &&
                    account.UserId == userId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!owned)
        {
            throw new InvalidOperationException(
                "The exchange account is unavailable in the user scope for REST ordering evidence.");
        }

        dbContext.ExchangeRestOrderingIntervals.Add(new ExchangeRestOrderingIntervalEntity
        {
            Id = interval.Id,
            ExchangeAccountId = interval.ExchangeAccountId.Value,
            Kind = interval.Kind,
            Symbol = interval.Symbol,
            StartedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.StartedAt),
            CompletedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.CompletedAt),
            RecordedAt = TimestampCanonicalizer.ToUtcMicroseconds(interval.RecordedAt),
        });
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetPositionEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string symbol,
        DateTimeOffset observedAt,
        DateTimeOffset? exchangeFactAt,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var normalizedObservedAt = TimestampCanonicalizer.ToUtcMicroseconds(observedAt);
        var trimmedSymbol = symbol.Trim();

        // Кандидаты: полные окна, пересекающие ObservedAt или биржевой факт, и scoped-окна с
        // completed_at >= метки (включая события раньше started_at того же scoped-окна).
        ExchangeRestOrderingIntervalEntity[] entities;
        if (TimestampCanonicalizer.ToUtcMicroseconds(exchangeFactAt) is { } normalizedExchangeAt)
        {
            entities = await OwnedIntervals(userId, exchangeAccountId)
                .Where(interval =>
                    (interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
                     ((interval.StartedAt <= normalizedObservedAt &&
                       interval.CompletedAt >= normalizedObservedAt) ||
                      (interval.StartedAt <= normalizedExchangeAt &&
                       interval.CompletedAt >= normalizedExchangeAt))) ||
                    (interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                     interval.Symbol != null &&
                     (interval.CompletedAt >= normalizedObservedAt ||
                      interval.CompletedAt >= normalizedExchangeAt)))
                .OrderBy(interval => interval.StartedAt)
                .ThenBy(interval => interval.CompletedAt)
                .ThenBy(interval => interval.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            entities = await OwnedIntervals(userId, exchangeAccountId)
                .Where(interval =>
                    (interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete &&
                     interval.StartedAt <= normalizedObservedAt &&
                     interval.CompletedAt >= normalizedObservedAt) ||
                    (interval.Kind == ExchangeRestOrderingKind.ScopedSymbol &&
                     interval.Symbol != null &&
                     interval.CompletedAt >= normalizedObservedAt))
                .OrderBy(interval => interval.StartedAt)
                .ThenBy(interval => interval.CompletedAt)
                .ThenBy(interval => interval.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return entities
            .Where(interval =>
                interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete ||
                string.Equals(interval.Symbol, trimmedSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(ToModel)
            .ToArray();
    }

    public async Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetBalanceEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset? observedAt,
        DateTimeOffset? messageCreatedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);

        var normalizedObservedAt = TimestampCanonicalizer.ToUtcMicroseconds(observedAt);
        var normalizedMessageCreatedAt = TimestampCanonicalizer.ToUtcMicroseconds(messageCreatedAt);

        if (normalizedObservedAt is null && normalizedMessageCreatedAt is null)
        {
            return [];
        }

        var query = OwnedIntervals(userId, exchangeAccountId)
            .Where(interval => interval.Kind == ExchangeRestOrderingKind.Balance)
            .Where(interval => interval.StartedAt < interval.CompletedAt);

        if (normalizedObservedAt is { } observed &&
            normalizedMessageCreatedAt is { } messageCreated)
        {
            query = query.Where(interval =>
                (interval.StartedAt < observed &&
                 observed < interval.CompletedAt) ||
                (interval.StartedAt < messageCreated &&
                 messageCreated < interval.CompletedAt));
        }
        else if (normalizedObservedAt is { } observedOnly)
        {
            query = query.Where(interval =>
                interval.StartedAt < observedOnly &&
                observedOnly < interval.CompletedAt);
        }
        else if (normalizedMessageCreatedAt is { } messageOnly)
        {
            query = query.Where(interval =>
                interval.StartedAt < messageOnly &&
                messageOnly < interval.CompletedAt);
        }

        var candidates = await query
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Select(ToModel)
            .ToArray();
    }

    public async Task<bool> HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string symbol,
        DateTimeOffset acceptedRestObservationAt,
        DateTimeOffset streamObservedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var normalizedAcceptedAt = TimestampCanonicalizer.ToUtcMicroseconds(acceptedRestObservationAt);
        var normalizedStreamAt = TimestampCanonicalizer.ToUtcMicroseconds(streamObservedAt);
        var trimmedSymbol = symbol.Trim();

        // StartedAt интервала совпадает с принятой меткой REST-наблюдения позиции. Только такое
        // совпадение связывает evidence с актуальным состоянием позиции, а не с более старой сверкой.
        var entities = await OwnedIntervals(userId, exchangeAccountId)
            .Where(interval =>
                interval.StartedAt == normalizedAcceptedAt &&
                interval.CompletedAt < normalizedStreamAt &&
                (interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete ||
                 interval.Kind == ExchangeRestOrderingKind.ScopedSymbol))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return entities.Any(interval =>
            interval.Kind == ExchangeRestOrderingKind.FullPositionsComplete ||
            string.Equals(interval.Symbol, trimmedSymbol, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> HasBalanceEvidenceCompletingAtAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsureAccountId(exchangeAccountId);

        var normalizedCompletedAt = TimestampCanonicalizer.ToUtcMicroseconds(completedAt);
        return await OwnedIntervals(userId, exchangeAccountId)
            .AnyAsync(
                interval =>
                    interval.Kind == ExchangeRestOrderingKind.Balance &&
                    interval.CompletedAt == normalizedCompletedAt,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<ExchangeRestOrderingIntervalEntity> OwnedIntervals(
        UserId userId,
        ExchangeAccountId exchangeAccountId) =>
        dbContext.ExchangeRestOrderingIntervals
            .AsNoTracking()
            .Where(interval =>
                interval.ExchangeAccountId == exchangeAccountId.Value &&
                dbContext.ExchangeAccounts.Any(account =>
                    account.Id == interval.ExchangeAccountId &&
                    account.UserId == userId.Value));

    private static ExchangeRestOrderingInterval ToModel(ExchangeRestOrderingIntervalEntity entity) =>
        new()
        {
            Id = entity.Id,
            ExchangeAccountId = ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
            Kind = entity.Kind,
            Symbol = entity.Symbol,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt,
            RecordedAt = entity.RecordedAt,
        };

    private static void ValidateInterval(ExchangeRestOrderingInterval interval)
    {
        if (interval.StartedAt > interval.CompletedAt)
        {
            throw new ArgumentException(
                "REST ordering interval requires StartedAt <= CompletedAt.",
                nameof(interval));
        }

        switch (interval.Kind)
        {
            case ExchangeRestOrderingKind.ScopedSymbol:
                if (string.IsNullOrWhiteSpace(interval.Symbol))
                {
                    throw new ArgumentException(
                        "ScopedSymbol evidence requires a non-empty symbol.",
                        nameof(interval));
                }

                break;

            case ExchangeRestOrderingKind.FullPositionsComplete:
            case ExchangeRestOrderingKind.Balance:
                if (interval.Symbol is not null)
                {
                    throw new ArgumentException(
                        $"{interval.Kind} evidence must not carry a symbol.",
                        nameof(interval));
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    interval.Kind,
                    "Unknown ordering kind.");
        }
    }

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
