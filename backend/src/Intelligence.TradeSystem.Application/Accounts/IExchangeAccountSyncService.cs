using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountSyncService
{
    Task<ExchangeAccountSyncResult> SynchronizeAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);
}
