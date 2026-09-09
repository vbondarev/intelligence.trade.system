using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Projects a newly-created domain history record into one application event.
/// </summary>
public static class PositionApplicationEventFactory
{
    public static IApplicationEvent Create(
        UserId userId,
        ExchangeAccount account,
        Position position,
        PositionChange change)
    {
        return Create(
            userId,
            account,
            position,
            change,
            ResolveSequence(position, change));
    }

    public static IApplicationEvent Create(
        UserId userId,
        ExchangeAccount account,
        Position position,
        PositionChange change,
        int positionChangeSequence)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(positionChangeSequence);

        var key = position.ExchangePositionKey;
        var before = change.Before is null
            ? null
            : PositionStateEventPayloadV1.From(change.Before);
        var after = PositionStateEventPayloadV1.From(change.After);
        var firstDetectedAt = position.FirstDetectedAt;
        var lastObservedAt = change.Kind == PositionChangeKind.New
            ? change.OccurredAt
            : position.LastObservedAt;
        var closedAt = change.TrackingStateAfter == PositionTrackingState.Closed
            ? position.ClosedAt
            : null;

        return change.Kind switch
        {
            PositionChangeKind.New => new PositionOpenedEventV1(
                Guid.NewGuid(),
                change.OccurredAt,
                userId.Value,
                account.Id.Value,
                account.ExchangeId,
                position.Id.Value,
                positionChangeSequence,
                key.InstrumentId.Value!,
                position.MarketCategory,
                key.PositionSide,
                key.PositionIdx,
                firstDetectedAt,
                lastObservedAt,
                closedAt,
                change.Kind,
                change.Cause,
                change.TrackingStateAfter,
                before,
                after),
            PositionChangeKind.Updated or
            PositionChangeKind.Increased or
            PositionChangeKind.Reduced or
            PositionChangeKind.MarkedUnknown or
            PositionChangeKind.MarkedStale or
            PositionChangeKind.Recovered => new PositionChangedEventV1(
                Guid.NewGuid(),
                change.OccurredAt,
                userId.Value,
                account.Id.Value,
                account.ExchangeId,
                position.Id.Value,
                positionChangeSequence,
                key.InstrumentId.Value!,
                position.MarketCategory,
                key.PositionSide,
                key.PositionIdx,
                firstDetectedAt,
                lastObservedAt,
                closedAt,
                change.Kind,
                change.Cause,
                change.TrackingStateAfter,
                before,
                after),
            PositionChangeKind.Closed => new PositionClosedEventV1(
                Guid.NewGuid(),
                change.OccurredAt,
                userId.Value,
                account.Id.Value,
                account.ExchangeId,
                position.Id.Value,
                positionChangeSequence,
                key.InstrumentId.Value!,
                position.MarketCategory,
                key.PositionSide,
                key.PositionIdx,
                firstDetectedAt,
                lastObservedAt,
                closedAt,
                change.Kind,
                change.Cause,
                change.TrackingStateAfter,
                before,
                after),
            _ => throw new ArgumentOutOfRangeException(
                nameof(change),
                change.Kind,
                "Position change kind is not mapped to an application event."),
        };
    }

    private static int ResolveSequence(Position position, PositionChange change)
    {
        for (var index = 0; index < position.Changes.Count; index++)
        {
            var candidate = position.Changes[index];
            if (ReferenceEquals(candidate, change) || candidate.Equals(change))
            {
                return index + 1;
            }
        }

        throw new InvalidOperationException(
            $"Position change for {position.Id} is not present in the position history.");
    }

    public static ExchangeAccountSyncDegradedEventV1 CreateSyncDegraded(
        UserId userId,
        ExchangeAccount account,
        string failureCategory,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCategory);
        if (failureCategory is not
            ("balance_failed" or "positions_failed" or "positions_partial" or "positions_ambiguous"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureCategory),
                failureCategory,
                "Failure category is not a safe synchronization category.");
        }

        return new(
            Guid.NewGuid(),
            occurredAt,
            userId.Value,
            account.Id.Value,
            account.ExchangeId,
            failureCategory,
            account.LastSyncedAt);
    }
}
