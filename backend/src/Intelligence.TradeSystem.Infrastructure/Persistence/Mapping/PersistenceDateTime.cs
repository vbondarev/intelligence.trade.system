namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PersistenceDateTime
{
    public static DateTimeOffset ToUtc(DateTimeOffset value) => value.ToUniversalTime();

    public static DateTimeOffset? ToUtc(DateTimeOffset? value) => value?.ToUniversalTime();
}
