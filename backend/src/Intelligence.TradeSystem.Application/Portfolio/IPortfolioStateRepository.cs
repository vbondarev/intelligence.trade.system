using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Portfolio;

public interface IPortfolioStateRepository
{
    Task<PortfolioState?> GetLatestAsync(
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(PortfolioState state, CancellationToken cancellationToken = default);
}
