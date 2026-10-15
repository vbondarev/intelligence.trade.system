using Intelligence.TradeSystem.Application.Time;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Вычисляет время новой сохранённой сводки без регресса порядка PostgreSQL snapshots.
/// </summary>
internal static class PortfolioStateCalculatedAt
{
    public static DateTimeOffset Resolve(DateTimeOffset candidate, DateTimeOffset? previous)
    {
        var normalizedCandidate = TimestampCanonicalizer.ToUtcMicroseconds(candidate);
        if (previous is not { } previousCalculatedAt)
        {
            return normalizedCandidate;
        }

        var normalizedPrevious = TimestampCanonicalizer.ToUtcMicroseconds(previousCalculatedAt);
        if (normalizedCandidate > normalizedPrevious)
        {
            return normalizedCandidate;
        }

        if (normalizedPrevious.Ticks > DateTimeOffset.MaxValue.Ticks - TimeSpan.TicksPerMicrosecond)
        {
            throw new InvalidOperationException(
                "PortfolioState.CalculatedAt cannot advance beyond the supported timestamp range.");
        }

        return new DateTimeOffset(
            normalizedPrevious.Ticks + TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);
    }
}
