using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio.Read;

public sealed class PortfolioReadService(IPortfolioReadStore store)
{
    public Task<PortfolioReadResult> GetLatestAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default) =>
        store.GetLatestAsync(userId, exchangeAccountId, cancellationToken);
}
