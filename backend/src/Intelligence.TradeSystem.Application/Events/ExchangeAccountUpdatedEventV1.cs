namespace Intelligence.TradeSystem.Application.Events;

public sealed record ExchangeAccountUpdatedEventV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid ExchangeAccountId) : IApplicationEvent
{
    public string EventType => ApplicationEventTypes.ExchangeAccountUpdated;

    public int SchemaVersion => ApplicationEventSchemaVersions.V1;
}
