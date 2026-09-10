namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Нейтральная информация об ошибке, возвращённая границей биржи.
/// </summary>
public sealed record ExchangeFailure(
    ExchangeFailureKind Kind,
    bool Retryable,
    string? ProviderCode = null);
