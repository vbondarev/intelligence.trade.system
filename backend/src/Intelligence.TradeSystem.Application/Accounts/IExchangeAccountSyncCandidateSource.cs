using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Enumerates active accounts for internal system work without exposing credentials.
/// </summary>
public interface IExchangeAccountSyncCandidateSource
{
    Task<IReadOnlyList<ExchangeAccountSyncCandidate>> GetBatchAsync(
        ExchangeAccountId? after,
        int limit,
        CancellationToken cancellationToken = default);
}
