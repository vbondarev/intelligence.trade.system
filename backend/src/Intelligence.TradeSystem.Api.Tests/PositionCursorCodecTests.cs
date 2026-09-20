using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class PositionCursorCodecTests
{
    [Fact]
    public void Cursor_round_trips_the_stable_position_order_key()
    {
        var cursor = new PositionReadCursor(
            new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.FromHours(3)),
            PositionId.New());

        var encoded = PositionCursorCodec.Encode(cursor);

        PositionCursorCodec.TryDecode(encoded, out var decoded).Should().BeTrue();
        decoded.FirstDetectedAt.Should().Be(cursor.FirstDetectedAt.ToUniversalTime());
        decoded.PositionId.Should().Be(cursor.PositionId);
        encoded.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AQ")]
    public void Cursor_rejects_malformed_values(string value)
    {
        PositionCursorCodec.TryDecode(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_rejects_a_valid_payload_with_an_unsupported_version()
    {
        var cursor = new PositionReadCursor(DateTimeOffset.UtcNow, PositionId.New());
        var encoded = PositionCursorCodec.Encode(cursor);
        var bytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(encoded);
        bytes[0] = 2;
        var unsupported = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes);

        PositionCursorCodec.TryDecode(unsupported, out _).Should().BeFalse();
    }
}
