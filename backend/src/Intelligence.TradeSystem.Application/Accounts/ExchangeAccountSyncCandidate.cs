using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Minimal system-scoped input for an internal background synchronization attempt.
/// </summary>
public sealed record ExchangeAccountSyncCandidate(
    UserId UserId,
    ExchangeAccountId ExchangeAccountId,
    DateTimeOffset? LastSyncedAt);
