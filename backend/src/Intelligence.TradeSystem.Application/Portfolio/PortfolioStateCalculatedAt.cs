namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Вычисляет время новой сохранённой сводки без регресса порядка PostgreSQL snapshots.
/// </summary>
/// <remarks>
/// Кандидат — нижняя граница, заданная временем включённых наблюдений, поэтому он округляется до
/// микросекунды хранения вверх: округление вниз дало бы время раньше наблюдения с долями микросекунды.
/// Сами времена наблюдений не изменяются.
/// </remarks>
internal static class PortfolioStateCalculatedAt
{
    public static DateTimeOffset Resolve(DateTimeOffset candidate, DateTimeOffset? previous)
    {
        var normalizedCandidate = CeilingToUtcMicroseconds(candidate);
        if (previous is not { } previousCalculatedAt)
        {
            return normalizedCandidate;
        }

        var normalizedPrevious = CeilingToUtcMicroseconds(previousCalculatedAt);
        if (normalizedCandidate > normalizedPrevious)
        {
            return normalizedCandidate;
        }

        return AddTicks(normalizedPrevious.UtcTicks, TimeSpan.TicksPerMicrosecond);
    }

    private static DateTimeOffset CeilingToUtcMicroseconds(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        var remainder = ticks % TimeSpan.TicksPerMicrosecond;
        return remainder == 0
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : AddTicks(ticks - remainder, TimeSpan.TicksPerMicrosecond);
    }

    private static DateTimeOffset AddTicks(long utcTicks, long ticks)
    {
        if (utcTicks > DateTimeOffset.MaxValue.UtcTicks - ticks)
        {
            throw new InvalidOperationException(
                "PortfolioState.CalculatedAt cannot advance beyond the supported timestamp range.");
        }

        return new DateTimeOffset(utcTicks + ticks, TimeSpan.Zero);
    }
}
