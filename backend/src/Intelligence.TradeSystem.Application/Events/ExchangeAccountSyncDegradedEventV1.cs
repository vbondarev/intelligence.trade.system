using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Events;

public sealed record ExchangeAccountSyncDegradedEventV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid ExchangeAccountId,
    ExchangeId ExchangeId,
    string FailureCategory,
    DateTimeOffset? LastSuccessfulSyncAt) : IApplicationEvent
{
    public string EventType => ApplicationEventTypes.ExchangeAccountSyncDegraded;

    public int SchemaVersion => ApplicationEventSchemaVersions.V1;
}
