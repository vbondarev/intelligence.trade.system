namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Durable application event contract.
/// </summary>
/// <remarks>
/// Delivery is at-least-once. Consumers must use <see cref="EventId"/> for idempotency
/// because a process can stop after a handler succeeds and before the outbox row is marked
/// processed.
/// </remarks>
public interface IApplicationEvent
{
    Guid EventId { get; }

    string EventType { get; }

    int SchemaVersion { get; }

    DateTimeOffset OccurredAt { get; }
}
