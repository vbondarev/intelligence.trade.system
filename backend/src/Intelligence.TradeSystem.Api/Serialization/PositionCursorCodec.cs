using System.Buffers.Binary;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace Intelligence.TradeSystem.Api.Serialization;

internal static class PositionCursorCodec
{
    private const byte CurrentVersion = 1;
    private const int EncodedLength = 1 + sizeof(long) + 16;

    public static string Encode(PositionReadCursor cursor)
    {
        Span<byte> bytes = stackalloc byte[EncodedLength];
        bytes[0] = CurrentVersion;
        BinaryPrimitives.WriteInt64BigEndian(
            bytes[1..(1 + sizeof(long))],
            cursor.FirstDetectedAt.UtcDateTime.Ticks);
        cursor.PositionId.Value.TryWriteBytes(bytes[(1 + sizeof(long))..]);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static bool TryDecode(string value, out PositionReadCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = WebEncoders.Base64UrlDecode(value);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != EncodedLength ||
            bytes[0] != CurrentVersion ||
            !string.Equals(EncodeBytes(bytes), value, StringComparison.Ordinal))
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(
            bytes.AsSpan(1, sizeof(long)));
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        var positionGuid = new Guid(bytes.AsSpan(1 + sizeof(long), 16));
        if (positionGuid == Guid.Empty)
        {
            return false;
        }

        cursor = new PositionReadCursor(
            new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc)),
            PositionId.FromGuid(positionGuid));
        return true;
    }

    private static string EncodeBytes(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);
}
