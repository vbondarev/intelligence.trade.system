using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

public sealed class ApplicationEventOutboxDispatcherOptionsValidator
    : IValidateOptions<ApplicationEventOutboxDispatcherOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ApplicationEventOutboxDispatcherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:PollingInterval must be greater than zero.");
        }
        else if (options.PollingInterval > ApplicationEventOutboxDispatcherOptions.MaximumPollingInterval)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:PollingInterval must not exceed fifteen minutes.");
        }

        if (options.BatchSize <= 0)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:BatchSize must be greater than zero.");
        }

        if (options.MaxConcurrency <= 0)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:MaxConcurrency must be greater than zero.");
        }

        if (options.ClaimDuration <= TimeSpan.Zero)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:ClaimDuration must be greater than zero.");
        }
        else if (options.ClaimDuration > ApplicationEventOutboxDispatcherOptions.MaximumClaimDuration)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:ClaimDuration must not exceed one hour.");
        }

        if (options.RetryBaseDelay <= TimeSpan.Zero)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:RetryBaseDelay must be greater than zero.");
        }
        else if (options.RetryBaseDelay > ApplicationEventOutboxDispatcherOptions.MaximumRetryBaseDelay)
        {
            failures.Add(
                "ApplicationEventOutboxDispatcher:RetryBaseDelay must not exceed one hour.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
