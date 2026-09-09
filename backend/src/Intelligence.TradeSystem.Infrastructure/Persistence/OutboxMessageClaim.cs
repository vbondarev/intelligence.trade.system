namespace Intelligence.TradeSystem.Infrastructure.Persistence;

public sealed record OutboxMessageClaim(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string Payload,
    DateTimeOffset CreatedAt,
    int AttemptCount,
    string ClaimedBy,
    Guid ClaimToken,
    DateTimeOffset ClaimedAt,
    DateTimeOffset ClaimExpiresAt);
