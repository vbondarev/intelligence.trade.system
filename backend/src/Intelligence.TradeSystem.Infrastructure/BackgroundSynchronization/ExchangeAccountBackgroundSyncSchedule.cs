namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public static class ExchangeAccountBackgroundSyncSchedule
{
    public static TimeSpan CalculateLag(
        DateTimeOffset plannedStart,
        DateTimeOffset actualStart) =>
        actualStart > plannedStart
            ? actualStart - plannedStart
            : TimeSpan.Zero;

    public static DateTimeOffset CalculateNextPlannedStart(
        DateTimeOffset plannedStart,
        DateTimeOffset now,
        TimeSpan interval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        var next = plannedStart + interval;
        if (next > now)
        {
            return next;
        }

        var elapsedTicks = (now - plannedStart).Ticks;
        var intervalsToSkip = checked(elapsedTicks / interval.Ticks + 1);
        var nextOffsetTicks = checked(interval.Ticks * intervalsToSkip);
        return plannedStart + TimeSpan.FromTicks(nextOffsetTicks);
    }
}
