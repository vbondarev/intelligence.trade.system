using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Application.Tests.Events;

public sealed class ApplicationEventContractTests
{
    private static readonly int[] LifecycleSequences = [1, 2, 3, 4];

    [Fact]
    public void Position_change_kinds_map_to_the_explicit_event_taxonomy()
    {
        var account = CreateAccount();
        var position = CreatePosition(account);
        var snapshot = new PositionStateSnapshot(
            1m,
            100m,
            100m,
            2m,
            100m,
            null,
            null,
            0m,
            null,
            null,
            null);

        var opened = PositionApplicationEventFactory.Create(
            account.UserId,
            account,
            position,
            new PositionChange(
                position.Id,
                PositionChangeKind.New,
                PositionChangeCause.InitialObservation,
                DateTimeOffset.UtcNow,
                PositionTrackingState.Active,
                null,
                snapshot),
             1);
        Assert.IsType<PositionOpenedEventV1>(opened);

        foreach (var kind in new[]
        {
            PositionChangeKind.Updated,
            PositionChangeKind.Increased,
            PositionChangeKind.Reduced,
            PositionChangeKind.MarkedUnknown,
            PositionChangeKind.MarkedStale,
            PositionChangeKind.Recovered,
        })
        {
            var changed = PositionApplicationEventFactory.Create(
                account.UserId,
                account,
                position,
                new PositionChange(
                    position.Id,
                    kind,
                    PositionChangeCause.ExchangeObservation,
                    DateTimeOffset.UtcNow,
                    PositionTrackingState.Active,
                    snapshot,
                    snapshot),
                 2);

            var changedEvent = Assert.IsType<PositionChangedEventV1>(changed);
            Assert.Equal(kind, changedEvent.PositionChangeKind);
        }

        var closed = PositionApplicationEventFactory.Create(
            account.UserId,
            account,
            position,
            new PositionChange(
                position.Id,
                PositionChangeKind.Closed,
                PositionChangeCause.MissingFromCompleteObservation,
                DateTimeOffset.UtcNow,
                PositionTrackingState.Closed,
                snapshot,
                snapshot),
             3);
        Assert.IsType<PositionClosedEventV1>(closed);
    }

    [Fact]
    public void Position_lifecycle_events_preserve_history_sequence_and_temporal_metadata()
    {
        var account = CreateAccount();
        var detectedAt = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        var position = CreatePosition(account, detectedAt);
        var increased = position.ApplyObservation(
            2m,
            detectedAt.AddMinutes(1),
            averageEntryPrice: 100m,
            positionValue: 200m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);
        var reduced = position.ApplyObservation(
            1m,
            detectedAt.AddMinutes(2),
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);
        var closed = position.Close(detectedAt.AddMinutes(3));

        var events = new IApplicationEvent[]
        {
            PositionApplicationEventFactory.Create(
                account.UserId,
                account,
                position,
                position.Changes[0]),
            PositionApplicationEventFactory.Create(
                account.UserId,
                account,
                position,
                increased!),
            PositionApplicationEventFactory.Create(
                account.UserId,
                account,
                position,
                reduced!),
            PositionApplicationEventFactory.Create(
                account.UserId,
                account,
                position,
                closed!),
        };

        Assert.Equal(
            LifecycleSequences,
            events.Select(GetPositionChangeSequence));
        foreach (var applicationEvent in events)
        {
            var serialized = ApplicationEventSerializer.Serialize(applicationEvent);
            Assert.Equal(
                applicationEvent,
                ApplicationEventSerializer.Deserialize(
                    serialized.EventType,
                    serialized.SchemaVersion,
                    serialized.Payload));
        }
        var opened = Assert.IsType<PositionOpenedEventV1>(events[0]);
        Assert.Equal(detectedAt, opened.FirstDetectedAt);
        Assert.Equal(detectedAt, opened.LastObservedAt);
        Assert.Null(opened.ClosedAt);
        var closedEvent = Assert.IsType<PositionClosedEventV1>(events[^1]);
        Assert.Equal(detectedAt.AddMinutes(3), closedEvent.ClosedAt);
        Assert.Equal(detectedAt.AddMinutes(2), closedEvent.LastObservedAt);
    }

    [Fact]
    public void Versioned_event_round_trips_without_domain_or_secret_fields()
    {
        var account = CreateAccount();
        var position = CreatePosition(account);
        var applicationEvent = PositionApplicationEventFactory.Create(
            account.UserId,
            account,
            position,
            position.Changes[0]);

        var serialized = ApplicationEventSerializer.Serialize(applicationEvent);
        var deserialized = ApplicationEventSerializer.Deserialize(
            serialized.EventType,
            serialized.SchemaVersion,
            serialized.Payload);

        Assert.Equal(applicationEvent, deserialized);
        foreach (var secretMarker in new[]
        {
            "api-key",
            "api-secret",
            "encrypted",
            "nonce",
            "tag",
            "Authorization",
            "provider error",
            "System.Exception",
        })
        {
            Assert.DoesNotContain(secretMarker, serialized.Payload, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("PositionChange", serialized.Payload, StringComparison.Ordinal);
        Assert.Contains("\"eventType\":\"position.opened\"", serialized.Payload, StringComparison.Ordinal);
        Assert.Contains("\"schemaVersion\":1", serialized.Payload, StringComparison.Ordinal);
        Assert.Equal(ApplicationEventTypes.PositionOpened, serialized.EventType);
        Assert.Equal(1, serialized.SchemaVersion);
    }

    [Fact]
    public void Unknown_contract_version_and_malformed_payload_are_not_accepted()
    {
        Assert.Throws<ApplicationEventSerializationException>(() =>
            ApplicationEventSerializer.Deserialize(
                ApplicationEventTypes.PositionOpened,
                2,
                "{}"));
        Assert.Throws<ApplicationEventSerializationException>(() =>
            ApplicationEventSerializer.Deserialize(
                ApplicationEventTypes.PositionOpened,
                1,
                "{"));
    }

    [Fact]
    public void Degraded_event_accepts_only_safe_failure_categories()
    {
        var account = CreateAccount();
        var degraded = PositionApplicationEventFactory.CreateSyncDegraded(
            account.UserId,
            account,
            "positions_partial",
            DateTimeOffset.UtcNow);

        Assert.Equal("positions_partial", degraded.FailureCategory);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PositionApplicationEventFactory.CreateSyncDegraded(
                account.UserId,
                account,
                "provider: raw secret",
                DateTimeOffset.UtcNow));
    }

    private static ExchangeAccount CreateAccount() =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            UserId.New(),
            ExchangeId.Bybit,
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance |
            ExchangeAccountCapabilities.ReadPositions);

    private static Position CreatePosition(ExchangeAccount account) =>
        CreatePosition(account, DateTimeOffset.UtcNow);

    private static Position CreatePosition(
        ExchangeAccount account,
        DateTimeOffset detectedAt) =>
        Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            detectedAt,
            detectedAt,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);

    private static int GetPositionChangeSequence(IApplicationEvent applicationEvent) =>
        applicationEvent switch
        {
            PositionOpenedEventV1 value => value.PositionChangeSequence,
            PositionChangedEventV1 value => value.PositionChangeSequence,
            PositionClosedEventV1 value => value.PositionChangeSequence,
            _ => throw new ArgumentOutOfRangeException(nameof(applicationEvent)),
        };
}
