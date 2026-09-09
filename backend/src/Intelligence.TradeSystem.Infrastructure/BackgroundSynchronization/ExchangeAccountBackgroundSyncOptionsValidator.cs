using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public sealed class ExchangeAccountBackgroundSyncOptionsValidator
    : IValidateOptions<ExchangeAccountBackgroundSyncOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ExchangeAccountBackgroundSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.Interval <= TimeSpan.Zero)
        {
            failures.Add(
                "ExchangeAccountBackgroundSync:Interval must be greater than zero.");
        }

        if (options.InitialDelay < TimeSpan.Zero)
        {
            failures.Add(
                "ExchangeAccountBackgroundSync:InitialDelay must be zero or greater.");
        }

        if (options.BatchSize <= 0)
        {
            failures.Add(
                "ExchangeAccountBackgroundSync:BatchSize must be greater than zero.");
        }

        if (options.MaxConcurrency <= 0)
        {
            failures.Add(
                "ExchangeAccountBackgroundSync:MaxConcurrency must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
