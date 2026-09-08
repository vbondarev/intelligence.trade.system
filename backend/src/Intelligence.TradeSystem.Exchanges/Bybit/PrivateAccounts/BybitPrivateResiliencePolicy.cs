using CryptoExchange.Net.Objects;
using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal static class BybitPrivateResiliencePolicy
{
    public const int MaxAttempts = 2;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RetryBackoff = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan MaxRateLimitRetryDelay = TimeSpan.FromSeconds(2);

    public static bool TryGetRetryDelay(
        ExchangeFailure failure,
        Error? error,
        out TimeSpan delay)
    {
        delay = TimeSpan.Zero;

        if (failure.Kind is ExchangeFailureKind.Timeout or ExchangeFailureKind.Unavailable)
        {
            delay = RetryBackoff + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 51));
            return true;
        }

        if (failure.Kind != ExchangeFailureKind.RateLimited
            || error is not BaseRateLimitError { RetryAfter: not null } rateLimitError)
        {
            return false;
        }

        delay = rateLimitError.RetryAfter.Value - DateTime.UtcNow;
        return delay > TimeSpan.Zero && delay <= MaxRateLimitRetryDelay;
    }
}
