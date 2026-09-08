using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Errors;
using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Exchanges.Bybit.PrivateAccounts;

internal static class BybitExchangeFailureMapper
{
    public static void ThrowIfCancellationRequested(Error? error, CancellationToken cancellationToken)
    {
        if (error?.ErrorType == ErrorType.CancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }
    }

    public static ExchangeFailure Map(Error? error)
    {
        if (error is null)
        {
            return new(ExchangeFailureKind.Unknown, Retryable: false);
        }

        var providerCode = string.IsNullOrWhiteSpace(error.ErrorCode)
            ? null
            : error.ErrorCode;

        var kind = providerCode switch
        {
            "10003" or "10004" or "-2015" or "33004" or "10007"
                => ExchangeFailureKind.InvalidCredentials,
            "10005" => ExchangeFailureKind.PermissionDenied,
            "10006" or "170222" or "429" => ExchangeFailureKind.RateLimited,
            _ => error.ErrorType switch
            {
                ErrorType.MissingCredentials => ExchangeFailureKind.InvalidCredentials,
                ErrorType.Unauthorized => ExchangeFailureKind.PermissionDenied,
                ErrorType.RateLimitRequest
                    or ErrorType.RateLimitConnection
                    or ErrorType.RateLimitSubscription
                    or ErrorType.RateLimitOrder => ExchangeFailureKind.RateLimited,
                ErrorType.Timeout => ExchangeFailureKind.Timeout,
                ErrorType.UnableToConnect
                    or ErrorType.NetworkError
                    or ErrorType.SystemError => ExchangeFailureKind.Unavailable,
                ErrorType.DeserializationFailed => ExchangeFailureKind.InvalidResponse,
                _ => ExchangeFailureKind.Unknown,
            },
        };

        var retryable = kind is
            ExchangeFailureKind.RateLimited
                or ExchangeFailureKind.Timeout
                or ExchangeFailureKind.Unavailable;

        return new(kind, retryable, providerCode);
    }

    public static string? SafeProviderCode(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)
            || providerCode.Length > 16
            || providerCode.Any(character => !char.IsDigit(character) && character != '-'))
        {
            return null;
        }

        return providerCode;
    }
}
