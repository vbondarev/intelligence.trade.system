using System.Diagnostics;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Accounts;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

internal static class ExchangeAccountBackgroundSyncTelemetry
{
    public const string ActivitySourceName =
        "Intelligence.TradeSystem.Infrastructure.BackgroundSync";
    public const string MeterName = ActivitySourceName;
    public const string WorkerName = "exchange-account-background-sync";
    public const string ExchangeName = "Bybit";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> SweepsStarted = Meter.CreateCounter<long>(
        "exchange.account.background_sync.sweeps.started",
        unit: "{sweep}");
    private static readonly Counter<long> SweepsCompleted = Meter.CreateCounter<long>(
        "exchange.account.background_sync.sweeps.completed",
        unit: "{sweep}");
    private static readonly Counter<long> Candidates = Meter.CreateCounter<long>(
        "exchange.account.background_sync.candidates",
        unit: "{candidate}");
    private static readonly Counter<long> Processed = Meter.CreateCounter<long>(
        "exchange.account.background_sync.processed",
        unit: "{account}");
    private static readonly Counter<long> UnexpectedFailures = Meter.CreateCounter<long>(
        "exchange.account.background_sync.unexpected_failures",
        unit: "{failure}");
    private static readonly Counter<long> NeverSynchronized = Meter.CreateCounter<long>(
        "exchange.account.background_sync.never_synchronized",
        unit: "{account}");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "exchange.account.background_sync.outcomes",
        unit: "{account}");
    private static readonly Histogram<double> SchedulerLag = Meter.CreateHistogram<double>(
        "exchange.account.background_sync.scheduler_lag",
        unit: "ms");
    private static readonly Histogram<double> SweepDuration = Meter.CreateHistogram<double>(
        "exchange.account.background_sync.sweep_duration",
        unit: "ms");
    private static readonly Histogram<double> LastSuccessfulSyncAge = Meter.CreateHistogram<double>(
        "exchange.account.background_sync.last_successful_sync_age",
        unit: "s");

    public static Activity? StartSweepActivity(TimeSpan schedulerLag)
    {
        var activity = ActivitySource.StartActivity(
            "exchange.account.background_sync.sweep",
            ActivityKind.Internal);
        activity?.SetTag("worker", WorkerName);
        activity?.SetTag("exchange", ExchangeName);
        activity?.SetTag("scheduler.lag_ms", schedulerLag.TotalMilliseconds);
        return activity;
    }

    public static void RecordSweepStarted(TimeSpan schedulerLag)
    {
        SweepsStarted.Add(1, CreateTags());
        SchedulerLag.Record(schedulerLag.TotalMilliseconds, CreateTags());
    }

    public static void RecordSweepCompleted(
        TimeSpan duration,
        ExchangeAccountBackgroundSyncSweepResult result)
    {
        SweepsCompleted.Add(1, CreateTags());
        SweepDuration.Record(duration.TotalMilliseconds, CreateTags());
        Candidates.Add(result.CandidateCount, CreateTags());
        Processed.Add(result.ProcessedCount, CreateTags());
        UnexpectedFailures.Add(result.UnexpectedFailureCount, CreateTags());
    }

    public static void RecordAccountAttempt(
        ExchangeAccountSyncOutcome outcome,
        double? lastSuccessfulSyncAge)
    {
        Outcomes.Add(
            1,
            CreateTags(("outcome", outcome.ToString())));

        if (lastSuccessfulSyncAge is { } age)
        {
            LastSuccessfulSyncAge.Record(age, CreateTags());
        }
        else
        {
            NeverSynchronized.Add(1, CreateTags());
        }
    }

    private static KeyValuePair<string, object?>[] CreateTags(
        params (string Key, object? Value)[] additionalTags)
    {
        var tags = new List<KeyValuePair<string, object?>>
        {
            new("worker", WorkerName),
            new("exchange", ExchangeName),
        };
        tags.AddRange(additionalTags.Select(tag => new KeyValuePair<string, object?>(
            tag.Key,
            tag.Value)));
        return tags.ToArray();
    }
}
