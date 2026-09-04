namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PersistenceDateTime
{
    private const long TicksPerPostgreSqlMicrosecond = TimeSpan.TicksPerMicrosecond;

    public static DateTimeOffset ToUtc(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - utc.Ticks % TicksPerPostgreSqlMicrosecond;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    public static DateTimeOffset? ToUtc(DateTimeOffset? value) =>
        value is { } timestamp ? ToUtc(timestamp) : null;
}
