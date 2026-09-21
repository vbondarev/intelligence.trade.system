using System.Buffers.Binary;
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

    [Fact]
    public void Cursor_rejects_invalid_version_lengths_identities_sequences_and_timestamps()
    {
        var evaluation = EncodeBytes(new PositionTimelineCursor(
            DateTimeOffset.UtcNow,
            PositionTimelineItemKind.Evaluation,
            PositionAssessmentId.New().Value,
            null));
        var positionChange = EncodeBytes(new PositionTimelineCursor(
            DateTimeOffset.UtcNow,
            PositionTimelineItemKind.PositionChange,
            null,
            1));

        var unsupportedVersion = evaluation.ToArray();
        unsupportedVersion[0] = 2;
        var emptyGuid = evaluation.ToArray();
        Array.Clear(emptyGuid, 1 + sizeof(long) + sizeof(byte), 16);
        var invalidTimestamp = evaluation.ToArray();
        BinaryPrimitives.WriteInt64BigEndian(
            invalidTimestamp.AsSpan(1, sizeof(long)),
            long.MaxValue);
        var nonPositiveSequence = positionChange.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(
            nonPositiveSequence.AsSpan(1 + sizeof(long) + sizeof(byte), sizeof(int)),
            0);

        var invalidValues = new[]
        {
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(unsupportedVersion),
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(emptyGuid),
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(invalidTimestamp),
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(nonPositiveSequence),
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(evaluation[..^1]),
            Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode([.. evaluation, 0]),
            PositionTimelineCursorCodec.Encode(new PositionTimelineCursor(
                DateTimeOffset.UtcNow,
                PositionTimelineItemKind.Evaluation,
                PositionAssessmentId.New().Value,
                null)) + "=",
        };

        foreach (var value in invalidValues)
        {
            PositionTimelineCursorCodec.TryDecode(value, out _).Should().BeFalse();
        }
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

    private static byte[] EncodeBytes(PositionTimelineCursor cursor) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(
            PositionTimelineCursorCodec.Encode(cursor));
}
