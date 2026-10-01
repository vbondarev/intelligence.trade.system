using FluentAssertions;
using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionTimelineMapperTests
{
    [Theory]
    [InlineData(PositionChangeKind.New, PositionChangeKindV1.New)]
    [InlineData(PositionChangeKind.Updated, PositionChangeKindV1.Updated)]
    [InlineData(PositionChangeKind.Increased, PositionChangeKindV1.Increased)]
    [InlineData(PositionChangeKind.Reduced, PositionChangeKindV1.Reduced)]
    [InlineData(PositionChangeKind.Closed, PositionChangeKindV1.Closed)]
    [InlineData(PositionChangeKind.MarkedUnknown, PositionChangeKindV1.MarkedUnknown)]
    [InlineData(PositionChangeKind.MarkedStale, PositionChangeKindV1.MarkedStale)]
    [InlineData(PositionChangeKind.Recovered, PositionChangeKindV1.Recovered)]
    public void ToResponse_maps_position_change_kind(
        PositionChangeKind kind,
        PositionChangeKindV1 expected)
    {
        var response = Map(kind, PositionChangeCause.ExchangeObservation);

        response.Items.Single().PositionChange!.Kind.Should().Be(expected);
    }

    [Theory]
    [InlineData(
        PositionChangeCause.InitialObservation,
        PositionChangeCauseV1.InitialObservation)]
    [InlineData(
        PositionChangeCause.ExchangeObservation,
        PositionChangeCauseV1.ExchangeObservation)]
    [InlineData(
        PositionChangeCause.MissingFromCompleteObservation,
        PositionChangeCauseV1.MissingFromCompleteObservation)]
    [InlineData(
        PositionChangeCause.PositionsObservationFailed,
        PositionChangeCauseV1.PositionsObservationFailed)]
    [InlineData(PositionChangeCause.PartialObservation, PositionChangeCauseV1.PartialObservation)]
    [InlineData(PositionChangeCause.FreshnessExpired, PositionChangeCauseV1.FreshnessExpired)]
    [InlineData(
        PositionChangeCause.ObservationRestored,
        PositionChangeCauseV1.ObservationRestored)]
    public void ToResponse_maps_position_change_cause(
        PositionChangeCause cause,
        PositionChangeCauseV1 expected)
    {
        var response = Map(PositionChangeKind.Updated, cause);

        response.Items.Single().PositionChange!.Cause.Should().Be(expected);
    }

    [Fact]
    public void ToResponse_rejects_unsupported_position_change_kind()
    {
        var map = () => Map((PositionChangeKind)999, PositionChangeCause.ExchangeObservation);

        map.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ToResponse_rejects_unsupported_position_change_cause()
    {
        var map = () => Map(PositionChangeKind.Updated, (PositionChangeCause)999);

        map.Should().Throw<NotSupportedException>();
    }

    private static CursorPage<PositionTimelineItemResponse> Map(
        PositionChangeKind kind,
        PositionChangeCause cause)
    {
        var occurredAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var change = new PositionTimelinePositionChange(
            1,
            kind,
            cause,
            PositionTrackingState.Active,
            null,
            new PositionTimelinePositionSnapshot(
                1m,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        var page = new PositionTimelinePage(
            [PositionTimelineItem.ForPositionChange(occurredAt, change)],
            null,
            false);

        return PositionTimelineMapper.ToResponse(page);
    }
}
