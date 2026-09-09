namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public interface IOutboxMessageStore
{
    Task<IReadOnlyList<OutboxMessageClaim>> ClaimAsync(
        int batchSize,
        string claimedBy,
        DateTimeOffset now,
        TimeSpan claimDuration,
        CancellationToken cancellationToken = default);

    Task<bool> MarkProcessedAsync(
        OutboxMessageClaim claim,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default);

    Task<bool> ScheduleRetryAsync(
        OutboxMessageClaim claim,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken = default);
}
