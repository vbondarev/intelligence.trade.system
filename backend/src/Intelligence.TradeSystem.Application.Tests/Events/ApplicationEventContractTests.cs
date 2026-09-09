using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Xunit;

namespace Intelligence.TradeSystem.Application.Tests.Events;

public sealed class ApplicationEventContractTests
{
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
                snapshot));
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
                    snapshot));

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
                snapshot));
        Assert.IsType<PositionClosedEventV1>(closed);
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
        Assert.DoesNotContain("api-secret", serialized.Payload, StringComparison.Ordinal);
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
        Position.Create(
            ExchangePositionKey.Create(
                account.Id,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 100m,
            unrealizedPnl: 0m);
}
