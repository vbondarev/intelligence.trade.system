namespace Intelligence.TradeSystem.Application.Events;

public sealed record PortfolioUpdatedEventV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid ExchangeAccountId) : IApplicationEvent
{
    public string EventType => ApplicationEventTypes.PortfolioUpdated;

    public int SchemaVersion => ApplicationEventSchemaVersions.V1;
}
