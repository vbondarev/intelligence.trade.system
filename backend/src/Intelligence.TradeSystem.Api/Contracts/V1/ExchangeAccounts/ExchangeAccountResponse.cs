namespace Intelligence.TradeSystem.Api.Contracts.V1.ExchangeAccounts;

/// <summary>Безопасное v1-представление биржевого аккаунта.</summary>
public sealed record ExchangeAccountResponse(
    Guid Id,
    ExchangeProvider Exchange,
    ExchangeAccountStatus ConnectionStatus,
    IReadOnlyList<ExchangeAccountCapability> Capabilities,
    DateTimeOffset? LastSyncedAt);
