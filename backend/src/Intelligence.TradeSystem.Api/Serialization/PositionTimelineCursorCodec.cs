using System.Buffers.Binary;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelligence.TradeSystem.Api.Serialization;

internal static class PositionTimelineCursorCodec
{
    private const byte CurrentVersion = 1;
    private const byte PositionChangeRank = 10;
    private const byte EvaluationRank = 20;
    private const byte RecommendationRank = 30;
    private const int HeaderLength = 1 + sizeof(long) + sizeof(byte);
    private const int PositionChangeLength = HeaderLength + sizeof(int);
    private const int GuidLength = HeaderLength + 16;

    public static string Encode(PositionTimelineCursor cursor)
    {
        var typeRank = ToTypeRank(cursor.Kind);
        var bytes = cursor.Kind == PositionTimelineItemKind.PositionChange
            ? EncodePositionChange(cursor, typeRank)
            : EncodeGuidSource(cursor, typeRank);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static bool TryDecode(string value, out PositionTimelineCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        byte[] bytes;
        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length < HeaderLength ||
            bytes[0] != CurrentVersion ||
            !string.Equals(WebEncoders.Base64UrlEncode(bytes), value, StringComparison.Ordinal))
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(1, sizeof(long)));
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;

        var occurredAt = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
        switch (bytes[1 + sizeof(long)])
        {
            case PositionChangeRank when bytes.Length == PositionChangeLength:
            {
                var sequence = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(HeaderLength, sizeof(int)));
                if (sequence <= 0)
                    return false;

                cursor = new PositionTimelineCursor(
                    occurredAt,
                    PositionTimelineItemKind.PositionChange,
                    null,
                    sequence);
                return true;
            }
            case EvaluationRank when bytes.Length == GuidLength:
                return TryCreateGuidCursor(
                    bytes,
                    occurredAt,
                    PositionTimelineItemKind.Evaluation,
                    out cursor);
            case RecommendationRank when bytes.Length == GuidLength:
                return TryCreateGuidCursor(
                    bytes,
                    occurredAt,
                    PositionTimelineItemKind.Recommendation,
                    out cursor);
            default:
                return false;
        }
    }

    private static byte[] EncodePositionChange(PositionTimelineCursor cursor, byte typeRank)
    {
        var bytes = CreateHeader(cursor, typeRank, PositionChangeLength);
        BinaryPrimitives.WriteInt32BigEndian(
            bytes.AsSpan(HeaderLength, sizeof(int)),
            cursor.PositionChangeSequence!.Value);
        return bytes;
    }

    private static byte[] EncodeGuidSource(PositionTimelineCursor cursor, byte typeRank)
    {
        var bytes = CreateHeader(cursor, typeRank, GuidLength);
        cursor.SourceId!.Value.TryWriteBytes(bytes.AsSpan(HeaderLength, 16));
        return bytes;
    }

    private static byte[] CreateHeader(PositionTimelineCursor cursor, byte typeRank, int length)
    {
        var bytes = new byte[length];
        bytes[0] = CurrentVersion;
        BinaryPrimitives.WriteInt64BigEndian(
            bytes.AsSpan(1, sizeof(long)),
            cursor.OccurredAt.UtcDateTime.Ticks);
        bytes[1 + sizeof(long)] = typeRank;
        return bytes;
    }

    private static bool TryCreateGuidCursor(
        byte[] bytes,
        DateTimeOffset occurredAt,
        PositionTimelineItemKind kind,
        out PositionTimelineCursor cursor)
    {
        cursor = default;
        var sourceId = new Guid(bytes.AsSpan(HeaderLength, 16));
        if (sourceId == Guid.Empty)
            return false;

        cursor = new PositionTimelineCursor(occurredAt, kind, sourceId, null);
        return true;
    }

    private static byte ToTypeRank(PositionTimelineItemKind kind) => kind switch
    {
        PositionTimelineItemKind.PositionChange => PositionChangeRank,
        PositionTimelineItemKind.Evaluation => EvaluationRank,
        PositionTimelineItemKind.Recommendation => RecommendationRank,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Timeline item kind must be defined."),
    };
}
