namespace Intelligence.TradeSystem.Application.Time;

public static class TimestampCanonicalizer
{
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMicrosecond;

    public static DateTimeOffset ToUtcMicroseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TicksPerMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static DateTimeOffset? ToUtcMicroseconds(DateTimeOffset? value) =>
        value is { } timestamp ? ToUtcMicroseconds(timestamp) : null;
}
