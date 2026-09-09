namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class OutboxMessageEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = null!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Payload { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
