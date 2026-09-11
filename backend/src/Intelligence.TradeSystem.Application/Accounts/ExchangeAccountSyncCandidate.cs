using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Минимальный вход с системной областью видимости для внутренней попытки фоновой синхронизации.
/// </summary>
public sealed record ExchangeAccountSyncCandidate(
    UserId UserId,
    ExchangeAccountId ExchangeAccountId,
    DateTimeOffset? LastSyncedAt);
