namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string MarketDataUnavailable = "market_data_unavailable";
    public const string ExchangeCredentialsInvalid = "exchange_credentials_invalid";
    public const string ExchangePermissionsRejected = "exchange_permissions_rejected";
    public const string ExchangeUnavailable = "exchange_unavailable";
    public const string InternalError = "internal_error";
}
