namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Neutral failure information returned by an exchange boundary.
/// </summary>
public sealed record ExchangeFailure(
    ExchangeFailureKind Kind,
    bool Retryable,
    string? ProviderCode = null);
