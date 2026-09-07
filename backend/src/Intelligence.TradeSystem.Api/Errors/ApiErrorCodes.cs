namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string MarketDataUnavailable = "market_data_unavailable";
    public const string InternalError = "internal_error";
}
