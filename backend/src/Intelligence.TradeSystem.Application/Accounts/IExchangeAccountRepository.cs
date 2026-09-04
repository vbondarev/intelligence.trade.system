using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountRepository
{
    Task<ExchangeAccount?> GetByIdAsync(ExchangeAccountId id, CancellationToken cancellationToken = default);

    Task SaveAsync(ExchangeAccount account, CancellationToken cancellationToken = default);
}
