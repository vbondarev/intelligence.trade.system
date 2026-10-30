using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

public sealed class ExchangeRealtimeObservationOptionsValidator
    : IValidateOptions<ExchangeRealtimeObservationOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        ExchangeRealtimeObservationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        if (options.QueueCapacity <= 0)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:QueueCapacity должен быть больше нуля.");
        }

        if (options.AccountConsumerCount <= 0)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:AccountConsumerCount должен быть больше нуля.");
        }

        if (options.ReconnectInitialDelay <= TimeSpan.Zero)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:ReconnectInitialDelay должен быть больше нуля.");
        }

        if (options.ReconnectMaxDelay <= TimeSpan.Zero)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:ReconnectMaxDelay должен быть больше нуля.");
        }
        else if (options.ReconnectMaxDelay < options.ReconnectInitialDelay)
        {
            failures.Add(
                "Параметр ExchangeRealtimeObservation:ReconnectMaxDelay должен быть не меньше ReconnectInitialDelay.");
        }

        if (!double.IsFinite(options.ReconnectJitter)
            || options.ReconnectJitter is < 0 or > 1)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:ReconnectJitter должен быть в диапазоне от 0 до 1.");
        }

        if (options.AccountDiscoveryInterval <= TimeSpan.Zero)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:AccountDiscoveryInterval должен быть больше нуля.");
        }

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            failures.Add("Параметр ExchangeRealtimeObservation:HeartbeatInterval должен быть больше нуля.");
        }

        if (options.HeartbeatStaleAfter <= options.HeartbeatInterval)
        {
            failures.Add(
                "Параметр ExchangeRealtimeObservation:HeartbeatStaleAfter должен быть больше HeartbeatInterval.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
