using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Events;

public sealed record PositionChangedEventV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid ExchangeAccountId,
    ExchangeId Exchange,
    Guid PositionId,
    string InstrumentId,
    MarketCategory MarketCategory,
    PositionSide PositionSide,
    int PositionIdx,
    PositionChangeKind PositionChangeKind,
    PositionChangeCause PositionChangeCause,
    PositionTrackingState TrackingStateAfter,
    PositionStateEventPayloadV1? Before,
    PositionStateEventPayloadV1 After) : IApplicationEvent
{
    public string EventType => ApplicationEventTypes.PositionChanged;

    public int SchemaVersion => ApplicationEventSchemaVersions.V1;
}
