using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Explicit registry and JSON serializer for durable application event contracts.
/// </summary>
public static class ApplicationEventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static SerializedApplicationEvent Serialize(IApplicationEvent applicationEvent)
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        return applicationEvent switch
        {
            PositionOpenedEventV1 value => Serialize(
                value.EventType,
                value.SchemaVersion,
                value.OccurredAt,
                value),
            PositionChangedEventV1 value => Serialize(
                value.EventType,
                value.SchemaVersion,
                value.OccurredAt,
                value),
            PositionClosedEventV1 value => Serialize(
                value.EventType,
                value.SchemaVersion,
                value.OccurredAt,
                value),
            ExchangeAccountSyncDegradedEventV1 value => Serialize(
                value.EventType,
                value.SchemaVersion,
                value.OccurredAt,
                value),
            _ => throw new ApplicationEventSerializationException(
                $"Unknown application event CLR contract '{applicationEvent.GetType().Name}'.",
                applicationEvent.EventType,
                applicationEvent.SchemaVersion),
        };
    }

    public static IApplicationEvent Deserialize(
        string eventType,
        int schemaVersion,
        string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        ValidateEnvelopeMetadata(eventType, schemaVersion, payload);

        return (eventType, schemaVersion) switch
        {
            (ApplicationEventTypes.PositionOpened, ApplicationEventSchemaVersions.V1) =>
                Deserialize<PositionOpenedEventV1>(eventType, schemaVersion, payload),
            (ApplicationEventTypes.PositionChanged, ApplicationEventSchemaVersions.V1) =>
                Deserialize<PositionChangedEventV1>(eventType, schemaVersion, payload),
            (ApplicationEventTypes.PositionClosed, ApplicationEventSchemaVersions.V1) =>
                Deserialize<PositionClosedEventV1>(eventType, schemaVersion, payload),
            (ApplicationEventTypes.ExchangeAccountSyncDegraded, ApplicationEventSchemaVersions.V1) =>
                Deserialize<ExchangeAccountSyncDegradedEventV1>(eventType, schemaVersion, payload),
            _ => throw new ApplicationEventSerializationException(
                $"Unknown application event contract '{eventType}' version {schemaVersion}.",
                eventType,
                schemaVersion),
        };
    }

    private static SerializedApplicationEvent Serialize<TEvent>(
        string eventType,
        int schemaVersion,
        DateTimeOffset occurredAt,
        TEvent applicationEvent)
        where TEvent : IApplicationEvent
    {
        Validate(applicationEvent);
        return new(
            eventType,
            schemaVersion,
            occurredAt,
            JsonSerializer.Serialize(applicationEvent, JsonOptions));
    }

    private static TEvent Deserialize<TEvent>(
        string eventType,
        int schemaVersion,
        string payload)
        where TEvent : IApplicationEvent
    {
        try
        {
            var applicationEvent = JsonSerializer.Deserialize<TEvent>(payload, JsonOptions);
            if (applicationEvent is null)
            {
                throw new ApplicationEventSerializationException(
                    "Application event payload deserialized to null.",
                    eventType,
                    schemaVersion);
            }

            Validate(applicationEvent);
            return applicationEvent;
        }
        catch (JsonException exception)
        {
            throw new ApplicationEventSerializationException(
                "Application event payload is malformed.",
                eventType,
                schemaVersion,
                exception);
        }
    }

    private static void ValidateEnvelopeMetadata(
        string eventType,
        int schemaVersion,
        string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("eventType", out var payloadEventType) ||
                payloadEventType.GetString() != eventType ||
                !root.TryGetProperty("schemaVersion", out var payloadSchemaVersion) ||
                payloadSchemaVersion.GetInt32() != schemaVersion)
            {
                throw new ApplicationEventSerializationException(
                    "Application event payload metadata does not match the outbox metadata.",
                    eventType,
                    schemaVersion);
            }
        }
        catch (JsonException exception)
        {
            throw new ApplicationEventSerializationException(
                "Application event payload is malformed.",
                eventType,
                schemaVersion,
                exception);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or FormatException)
        {
            throw new ApplicationEventSerializationException(
                "Application event payload metadata has an invalid shape.",
                eventType,
                schemaVersion,
                exception);
        }
    }

    private static void Validate(IApplicationEvent applicationEvent)
    {
        if (applicationEvent.EventId == Guid.Empty)
            throw new ApplicationEventSerializationException(
                "Application event EventId must be initialized.",
                applicationEvent.EventType,
                applicationEvent.SchemaVersion);

        if (applicationEvent.OccurredAt == default)
            throw new ApplicationEventSerializationException(
                "Application event OccurredAt must be initialized.",
                applicationEvent.EventType,
                applicationEvent.SchemaVersion);

        switch (applicationEvent)
        {
            case PositionOpenedEventV1 opened:
                ValidatePositionEvent(opened);
                break;
            case PositionChangedEventV1 changed:
                ValidatePositionEvent(changed);
                break;
            case PositionClosedEventV1 closed:
                ValidatePositionEvent(closed);
                break;
            case ExchangeAccountSyncDegradedEventV1 value:
                if (value.UserId == Guid.Empty ||
                    value.ExchangeAccountId == Guid.Empty ||
                    !Enum.IsDefined(value.ExchangeId) ||
                    value.FailureCategory is not
                        ("balance_failed" or
                         "positions_failed" or
                         "positions_partial" or
                         "positions_ambiguous"))
                {
                    throw new ApplicationEventSerializationException(
                        "Exchange account degraded event contains invalid ownership or failure data.",
                        value.EventType,
                        value.SchemaVersion);
                }

                break;
        }
    }

    private static void ValidatePositionEvent(PositionOpenedEventV1 value)
    {
        ValidatePositionEventCore(
            value.EventType,
            value.SchemaVersion,
            value.UserId,
            value.ExchangeAccountId,
            value.Exchange,
            value.PositionId,
            value.PositionChangeSequence,
            value.InstrumentId,
            value.MarketCategory,
            value.PositionSide,
            value.PositionIdx,
            value.FirstDetectedAt,
            value.LastObservedAt,
            value.ClosedAt,
            value.PositionChangeKind,
            value.PositionChangeCause,
            value.TrackingStateAfter,
            value.After);
        if (value.PositionChangeKind != PositionChangeKind.New)
            throw new ApplicationEventSerializationException(
                "Position opened event must carry the New change kind.",
                value.EventType,
                value.SchemaVersion);
    }

    private static void ValidatePositionEvent(PositionChangedEventV1 value)
    {
        ValidatePositionEventCore(
            value.EventType,
            value.SchemaVersion,
            value.UserId,
            value.ExchangeAccountId,
            value.Exchange,
            value.PositionId,
            value.PositionChangeSequence,
            value.InstrumentId,
            value.MarketCategory,
            value.PositionSide,
            value.PositionIdx,
            value.FirstDetectedAt,
            value.LastObservedAt,
            value.ClosedAt,
            value.PositionChangeKind,
            value.PositionChangeCause,
            value.TrackingStateAfter,
            value.After);
        if (value.PositionChangeKind is PositionChangeKind.New or PositionChangeKind.Closed)
            throw new ApplicationEventSerializationException(
                "Position changed event must carry a non-terminal change kind.",
                value.EventType,
                value.SchemaVersion);
    }

    private static void ValidatePositionEvent(PositionClosedEventV1 value)
    {
        ValidatePositionEventCore(
            value.EventType,
            value.SchemaVersion,
            value.UserId,
            value.ExchangeAccountId,
            value.Exchange,
            value.PositionId,
            value.PositionChangeSequence,
            value.InstrumentId,
            value.MarketCategory,
            value.PositionSide,
            value.PositionIdx,
            value.FirstDetectedAt,
            value.LastObservedAt,
            value.ClosedAt,
            value.PositionChangeKind,
            value.PositionChangeCause,
            value.TrackingStateAfter,
            value.After);
        if (value.PositionChangeKind != PositionChangeKind.Closed)
            throw new ApplicationEventSerializationException(
                "Position closed event must carry the Closed change kind.",
                value.EventType,
                value.SchemaVersion);
    }

    private static void ValidatePositionEventCore(
        string eventType,
        int schemaVersion,
        Guid userId,
        Guid exchangeAccountId,
        ExchangeId exchange,
        Guid positionId,
        int positionChangeSequence,
        string? instrumentId,
        MarketCategory marketCategory,
        PositionSide positionSide,
        int positionIdx,
        DateTimeOffset firstDetectedAt,
        DateTimeOffset lastObservedAt,
        DateTimeOffset? closedAt,
        PositionChangeKind positionChangeKind,
        PositionChangeCause positionChangeCause,
        PositionTrackingState trackingStateAfter,
        PositionStateEventPayloadV1? after)
    {
        if (userId == Guid.Empty ||
            exchangeAccountId == Guid.Empty ||
            !Enum.IsDefined(exchange) ||
            positionId == Guid.Empty ||
            positionChangeSequence <= 0 ||
            string.IsNullOrWhiteSpace(instrumentId) ||
            !Enum.IsDefined(marketCategory) ||
            !Enum.IsDefined(positionSide) ||
            positionSide == PositionSide.Unknown ||
            positionIdx < 0 ||
            !Enum.IsDefined(positionChangeKind) ||
            !Enum.IsDefined(positionChangeCause) ||
            !Enum.IsDefined(trackingStateAfter) ||
            firstDetectedAt == default ||
            lastObservedAt == default ||
            lastObservedAt < firstDetectedAt ||
            (trackingStateAfter == PositionTrackingState.Closed
                ? closedAt is null || closedAt < lastObservedAt
                : closedAt is not null) ||
            after is null)
        {
            throw new ApplicationEventSerializationException(
                "Position event contains invalid identity, classification, or snapshot data.",
                eventType,
                schemaVersion);
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowTrailingCommas = false,
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
