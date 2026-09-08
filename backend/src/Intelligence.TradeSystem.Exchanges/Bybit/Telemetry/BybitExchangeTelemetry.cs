using System.Diagnostics;
using System.Diagnostics.Metrics;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Telemetry;

internal static class BybitExchangeTelemetry
{
    public const string ActivitySourceName = "Intelligence.TradeSystem.Exchanges.Bybit";
    public const string MeterName = "Intelligence.TradeSystem.Exchanges.Bybit";
    public const string ExchangeName = "Bybit";

    public const string BalanceOperation = "exchange.balance.fetch";
    public const string PositionsOperation = "exchange.positions.fetch";

    public const string SuccessOutcome = "success";
    public const string PartialOutcome = "partial";
    public const string FailureOutcome = "failure";
    public const string CancelledOutcome = "cancelled";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "exchange.requests",
        unit: "{request}");
    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "exchange.request.duration",
        unit: "ms");
    private static readonly Counter<long> RequestFailures = Meter.CreateCounter<long>(
        "exchange.request.failures",
        unit: "{failure}");

    public static Activity? StartActivity(string operation) =>
        ActivitySource.StartActivity(operation, ActivityKind.Client);

    public static void Record(
        Activity? activity,
        string operation,
        string outcome,
        TimeSpan elapsed,
        int retryCount,
        ExchangeFailure? failure,
        ExchangeFailure? retryFailure,
        MarketCategory? marketCategory = null,
        AccountType? accountType = null)
    {
        activity?.SetTag("exchange.name", ExchangeName);
        activity?.SetTag("exchange.operation", operation);
        if (marketCategory is not null)
        {
            activity?.SetTag("exchange.market_category", marketCategory.Value.ToString());
        }

        if (accountType is not null)
        {
            activity?.SetTag("exchange.account_type", accountType.Value.ToString());
        }

        activity?.SetTag("exchange.outcome", outcome);
        activity?.SetTag("exchange.failure_kind", failure?.Kind.ToString() ?? string.Empty);
        activity?.SetTag("exchange.retryable", failure?.Retryable ?? false);
        activity?.SetTag("retry.count", retryCount);
        activity?.SetTag("retry.failure_kind", retryFailure?.Kind.ToString() ?? string.Empty);

        var tags = CreateMetricTags(operation, outcome, failure, marketCategory, accountType);
        Requests.Add(1, tags);
        RequestDuration.Record(elapsed.TotalMilliseconds, tags);
        if (outcome is PartialOutcome or FailureOutcome)
        {
            RequestFailures.Add(1, tags);
        }
    }

    private static KeyValuePair<string, object?>[] CreateMetricTags(
        string operation,
        string outcome,
        ExchangeFailure? failure,
        MarketCategory? marketCategory,
        AccountType? accountType) =>
    [
        new("exchange", ExchangeName),
        new("operation", operation),
        new("outcome", outcome),
        new("failure_kind", failure?.Kind.ToString() ?? string.Empty),
        new("market_category", marketCategory?.ToString() ?? string.Empty),
        new("account_type", accountType?.ToString() ?? string.Empty),
    ];
}
