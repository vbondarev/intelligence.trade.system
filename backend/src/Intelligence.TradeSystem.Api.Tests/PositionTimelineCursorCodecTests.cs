using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionTimelineCursorCodecTests
{
    [Theory]
    [MemberData(nameof(Cursors))]
    public void Cursor_round_trips_canonical_timeline_order_keys(PositionTimelineCursor cursor)
    {
        var encoded = PositionTimelineCursorCodec.Encode(cursor);

        PositionTimelineCursorCodec.TryDecode(encoded, out var decoded).Should().BeTrue();
        decoded.OccurredAt.Should().Be(cursor.OccurredAt.ToUniversalTime());
        decoded.Kind.Should().Be(cursor.Kind);
        decoded.SourceId.Should().Be(cursor.SourceId);
        decoded.PositionChangeSequence.Should().Be(cursor.PositionChangeSequence);
        encoded.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQ")]
    public void Cursor_rejects_malformed_values(string value) =>
        PositionTimelineCursorCodec.TryDecode(value, out _).Should().BeFalse();

    [Fact]
    public void Cursor_rejects_a_valid_payload_with_an_unsupported_type_rank()
    {
        var encoded = PositionTimelineCursorCodec.Encode(
            new PositionTimelineCursor(
                DateTimeOffset.UtcNow,
                PositionTimelineItemKind.Evaluation,
                PositionAssessmentId.New().Value,
                null));
        var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encoded);
        bytes[1 + sizeof(long)] = 42;

        PositionTimelineCursorCodec.TryDecode(
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes),
            out _).Should().BeFalse();
    }

    public static TheoryData<PositionTimelineCursor> Cursors =>
    [
        new(
            new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.FromHours(3)),
            PositionTimelineItemKind.PositionChange,
            null,
            42),
        new(
            new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero),
            PositionTimelineItemKind.Evaluation,
            PositionAssessmentId.New().Value,
            null),
        new(
            new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero),
            PositionTimelineItemKind.Recommendation,
            RecommendationId.New().Value,
            null),
    ];
}
