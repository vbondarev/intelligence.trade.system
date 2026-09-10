using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

/// <summary>
/// Перечисляет активные учётные записи для внутренних системных операций, не раскрывая учётные данные.
/// </summary>
public interface IExchangeAccountSyncCandidateSource
{
    Task<IReadOnlyList<ExchangeAccountSyncCandidate>> GetBatchAsync(
        ExchangeAccountId? after,
        int limit,
        CancellationToken cancellationToken = default);
}
