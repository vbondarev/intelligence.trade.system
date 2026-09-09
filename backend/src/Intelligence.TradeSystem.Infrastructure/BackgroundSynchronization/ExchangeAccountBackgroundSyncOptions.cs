namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public sealed class ExchangeAccountBackgroundSyncOptions
{
    public const string SectionName = "ExchangeAccountBackgroundSync";
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaximumInitialDelay = TimeSpan.FromDays(1);

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(30);
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrency { get; set; } = 4;
}
