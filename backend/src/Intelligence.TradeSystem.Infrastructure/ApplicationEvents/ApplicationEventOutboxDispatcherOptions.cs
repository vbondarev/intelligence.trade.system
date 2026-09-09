namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

public sealed class ApplicationEventOutboxDispatcherOptions
{
    public const string SectionName = "ApplicationEventOutboxDispatcher";

    public static readonly TimeSpan MaximumPollingInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaximumClaimDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaximumRetryBaseDelay = TimeSpan.FromHours(1);

    public bool Enabled { get; set; } = true;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;
    public int MaxConcurrency { get; set; } = 4;
    public TimeSpan ClaimDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(10);
}
