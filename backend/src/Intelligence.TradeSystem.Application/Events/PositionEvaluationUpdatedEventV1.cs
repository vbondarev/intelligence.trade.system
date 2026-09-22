namespace Intelligence.TradeSystem.Application.Events;

public sealed record PositionEvaluationUpdatedEventV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid PositionId) : IApplicationEvent
{
    public string EventType => ApplicationEventTypes.PositionEvaluationUpdated;

    public int SchemaVersion => ApplicationEventSchemaVersions.V1;
}
