namespace Intelligence.TradeSystem.Api.Errors;

internal static class ApiErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string ResourceNotFound = "resource_not_found";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string PositionNotEvaluable = "position_not_evaluable";
    public const string MarketDataUnavailable = "market_data_unavailable";
    public const string ExchangeCredentialsInvalid = "exchange_credentials_invalid";
    public const string ExchangePermissionsRejected = "exchange_permissions_rejected";
    public const string ExchangeAccountDisabled = "exchange_account_disabled";
    public const string ExchangeAccountIdentityMismatch = "exchange_account_identity_mismatch";
    public const string ExchangeUnavailable = "exchange_unavailable";
    public const string InternalError = "internal_error";
}
