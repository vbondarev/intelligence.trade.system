using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed class ApplicationEventOutbox(
    TradeSystemDbContext dbContext,
    TimeProvider? timeProvider = null)
    : IApplicationEventOutbox, IOutboxMessageStore
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task AddAsync(
        IApplicationEvent applicationEvent,
        CancellationToken cancellationToken = default) =>
        AddRangeAsync([applicationEvent], cancellationToken);

    public async Task AddRangeAsync(
        IReadOnlyCollection<IApplicationEvent> applicationEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applicationEvents);
        if (applicationEvents.Count == 0)
            return;

        var duplicateEventId = applicationEvents
            .GroupBy(applicationEvent => applicationEvent.EventId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateEventId is not null)
        {
            throw new ArgumentException(
                $"Application event batch contains duplicate EventId {duplicateEventId.Key}.",
                nameof(applicationEvents));
        }

        var createdAt = ToUtc(clock.GetUtcNow());
        var messages = applicationEvents
            .Select(applicationEvent =>
            {
                var serialized = ApplicationEventSerializer.Serialize(applicationEvent);
                return new OutboxMessageEntity
                {
                    EventId = applicationEvent.EventId,
                    EventType = serialized.EventType,
                    SchemaVersion = serialized.SchemaVersion,
                    OccurredAt = ToUtc(serialized.OccurredAt),
                    Payload = serialized.Payload,
                    CreatedAt = createdAt,
                    AttemptCount = 0,
                    NextAttemptAt = createdAt,
                };
            })
            .ToArray();

        dbContext.OutboxMessages.AddRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OutboxMessageClaim>> ClaimAsync(
        int batchSize,
        string claimedBy,
        DateTimeOffset now,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(claimDuration, TimeSpan.Zero);

        var claimedAt = ToUtc(now);
        var claimExpiresAt = ToUtc(claimedAt.Add(claimDuration));
        var claimToken = Guid.NewGuid();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messages = await dbContext.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT event_id,
                           event_type,
                           schema_version,
                           occurred_at,
                           payload,
                           created_at,
                           attempt_count,
                           next_attempt_at,
                           claimed_at,
                           claimed_by,
                           claim_token,
                           claim_expires_at,
                           processed_at
                    FROM outbox_messages
                    WHERE processed_at IS NULL
                      AND next_attempt_at <= {claimedAt}
                      AND (claim_expires_at IS NULL OR claim_expires_at <= {claimedAt})
                    ORDER BY created_at, event_id
                    FOR UPDATE SKIP LOCKED
                    LIMIT {batchSize}
                    """)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var message in messages)
            {
                message.ClaimedAt = claimedAt;
                message.ClaimedBy = claimedBy;
                message.ClaimToken = claimToken;
                message.ClaimExpiresAt = claimExpiresAt;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return messages
                .Select(message => new OutboxMessageClaim(
                    message.EventId,
                    message.EventType,
                    message.SchemaVersion,
                    message.OccurredAt,
                    message.Payload,
                    message.CreatedAt,
                    message.AttemptCount,
                    message.ClaimedBy!,
                    message.ClaimToken!.Value,
                    message.ClaimedAt!.Value,
                    message.ClaimExpiresAt!.Value))
                .ToArray();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<bool> MarkProcessedAsync(
        OutboxMessageClaim claim,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var affected = await dbContext.OutboxMessages
            .Where(message =>
                message.EventId == claim.EventId &&
                message.ProcessedAt == null &&
                message.ClaimedBy == claim.ClaimedBy &&
                message.ClaimToken == claim.ClaimToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.ProcessedAt, ToUtc(processedAt))
                    .SetProperty(message => message.ClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.ClaimedBy, (string?)null)
                    .SetProperty(message => message.ClaimToken, (Guid?)null)
                    .SetProperty(message => message.ClaimExpiresAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1;
    }

    public async Task<bool> ScheduleRetryAsync(
        OutboxMessageClaim claim,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var affected = await dbContext.OutboxMessages
            .Where(message =>
                message.EventId == claim.EventId &&
                message.ProcessedAt == null &&
                message.ClaimedBy == claim.ClaimedBy &&
                message.ClaimToken == claim.ClaimToken)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1)
                    .SetProperty(message => message.NextAttemptAt, ToUtc(nextAttemptAt))
                    .SetProperty(message => message.ClaimedAt, (DateTimeOffset?)null)
                    .SetProperty(message => message.ClaimedBy, (string?)null)
                    .SetProperty(message => message.ClaimToken, (Guid?)null)
                    .SetProperty(message => message.ClaimExpiresAt, (DateTimeOffset?)null),
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1;
    }

    private static DateTimeOffset ToUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
