namespace Intelligence.TradeSystem.Application.Events;

public sealed record SerializedApplicationEvent(
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string Payload);
