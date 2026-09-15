namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PersistenceDateTime
{
    public static DateTimeOffset ToUtc(DateTimeOffset value)
        => Intelligence.TradeSystem.Application.Time.TimestampCanonicalizer.ToUtcMicroseconds(value);

    public static DateTimeOffset? ToUtc(DateTimeOffset? value) =>
        Intelligence.TradeSystem.Application.Time.TimestampCanonicalizer.ToUtcMicroseconds(value);
}
