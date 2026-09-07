using Microsoft.AspNetCore.Http;

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

    public static ApiErrorDescriptor InternalError { get; } = new(
        ApiErrorCodes.InternalError,
        StatusCodes.Status500InternalServerError,
        "An unexpected error occurred.",
        "urn:intelligence-trade:error:internal-error");
}
