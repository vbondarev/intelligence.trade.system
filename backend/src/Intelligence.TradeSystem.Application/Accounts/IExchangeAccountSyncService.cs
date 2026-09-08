using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountSyncService
{
    Task<ExchangeAccountSyncResult> SynchronizeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);
}
