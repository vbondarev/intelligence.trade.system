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
    private static readonly KeyValuePair<string, object?>[] BaseTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
    ];
    private static readonly KeyValuePair<string, object?>[] SynchronizedTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.Synchronized)),
    ];
    private static readonly KeyValuePair<string, object?>[] NotFoundTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.NotFound)),
    ];
    private static readonly KeyValuePair<string, object?>[] AccountDisabledTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.AccountDisabled)),
    ];
    private static readonly KeyValuePair<string, object?>[] CredentialsUnavailableTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.CredentialsUnavailable)),
    ];
    private static readonly KeyValuePair<string, object?>[] ExchangeUnavailableTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.ExchangeUnavailable)),
    ];
    private static readonly KeyValuePair<string, object?>[] AlreadyAppliedTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.AlreadyApplied)),
    ];
    private static readonly KeyValuePair<string, object?>[] SupersededTags =
    [
        new("worker", WorkerName),
        new("exchange", ExchangeName),
        new("outcome", nameof(ExchangeAccountSyncOutcome.Superseded)),
    ];

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
        SweepsStarted.Add(1, BaseTags);
        SchedulerLag.Record(schedulerLag.TotalMilliseconds, BaseTags);
    }

    public static void RecordSweepCompleted(
        TimeSpan duration,
        ExchangeAccountBackgroundSyncSweepResult result)
    {
        SweepsCompleted.Add(1, BaseTags);
        SweepDuration.Record(duration.TotalMilliseconds, BaseTags);
        Candidates.Add(result.CandidateCount, BaseTags);
        Processed.Add(result.ProcessedCount, BaseTags);
        UnexpectedFailures.Add(result.UnexpectedFailureCount, BaseTags);
    }

    public static void RecordAccountAttempt(
        ExchangeAccountSyncOutcome outcome,
        double? lastSuccessfulSyncAge)
    {
        Outcomes.Add(1, GetOutcomeTags(outcome));

        if (lastSuccessfulSyncAge is { } age)
        {
            LastSuccessfulSyncAge.Record(age, BaseTags);
        }
        else
        {
            NeverSynchronized.Add(1, BaseTags);
        }
    }

    public static DateTimeOffset? GetAuthoritativeLastSuccessfulSyncAt(
        ExchangeAccountSyncResult result) =>
        result.Account?.LastSyncedAt;

    private static KeyValuePair<string, object?>[] GetOutcomeTags(
        ExchangeAccountSyncOutcome outcome) =>
        outcome switch
        {
            ExchangeAccountSyncOutcome.Synchronized => SynchronizedTags,
            ExchangeAccountSyncOutcome.NotFound => NotFoundTags,
            ExchangeAccountSyncOutcome.AccountDisabled => AccountDisabledTags,
            ExchangeAccountSyncOutcome.CredentialsUnavailable => CredentialsUnavailableTags,
            ExchangeAccountSyncOutcome.ExchangeUnavailable => ExchangeUnavailableTags,
            ExchangeAccountSyncOutcome.AlreadyApplied => AlreadyAppliedTags,
            ExchangeAccountSyncOutcome.Superseded => SupersededTags,
            _ =>
            [
                new("worker", WorkerName),
                new("exchange", ExchangeName),
                new("outcome", outcome.ToString()),
            ],
        };

}
