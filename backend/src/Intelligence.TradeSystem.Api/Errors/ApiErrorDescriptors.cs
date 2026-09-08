namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiErrorDescriptors
{
    public static ApiErrorDescriptor ValidationFailed { get; } = new(
        ApiErrorCodes.ValidationFailed,
        StatusCodes.Status400BadRequest,
        "Request validation failed.",
        "urn:intelligence-trade:error:validation-failed");

    public static ApiErrorDescriptor ConcurrencyConflict { get; } = new(
        ApiErrorCodes.ConcurrencyConflict,
        StatusCodes.Status409Conflict,
        "Resource conflict.",
        "urn:intelligence-trade:error:concurrency-conflict");

    public static ApiErrorDescriptor MarketDataUnavailable { get; } = new(
        ApiErrorCodes.MarketDataUnavailable,
        StatusCodes.Status503ServiceUnavailable,
        "Market data is temporarily unavailable.",
        "urn:intelligence-trade:error:market-data-unavailable");

    public static ApiErrorDescriptor ExchangeCredentialsInvalid { get; } = new(
        ApiErrorCodes.ExchangeCredentialsInvalid,
        StatusCodes.Status400BadRequest,
        "Exchange credentials are invalid.",
        "urn:intelligence-trade:error:exchange-credentials-invalid");

    public static ApiErrorDescriptor ExchangePermissionsRejected { get; } = new(
        ApiErrorCodes.ExchangePermissionsRejected,
        StatusCodes.Status403Forbidden,
        "Exchange API key permissions are not allowed.",
        "urn:intelligence-trade:error:exchange-permissions-rejected");

    public static ApiErrorDescriptor ExchangeUnavailable { get; } = new(
        ApiErrorCodes.ExchangeUnavailable,
        StatusCodes.Status503ServiceUnavailable,
        "The exchange is temporarily unavailable.",
        "urn:intelligence-trade:error:exchange-unavailable");

    public static ApiErrorDescriptor InternalError { get; } = new(
        ApiErrorCodes.InternalError,
        StatusCodes.Status500InternalServerError,
        "An unexpected error occurred.",
        "urn:intelligence-trade:error:internal-error");
}
