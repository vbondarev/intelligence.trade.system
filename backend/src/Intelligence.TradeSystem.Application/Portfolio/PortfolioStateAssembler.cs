using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Нормализует сырое наблюдение баланса и собирает новый бизнес-снимок портфеля.
/// </summary>
public static class PortfolioStateAssembler
{
    public static PortfolioState Assemble(
        AccountBalance? balance,
        DateTimeOffset? balanceObservedAt,
        IReadOnlyCollection<Position> positions,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset calculatedAt,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var capital = balance is null
            ? new PortfolioCapitalState(null, null, null)
            : new PortfolioCapitalState(
                balance.TotalEquity,
                balance.TotalAvailableBalance,
                balanceObservedAt,
                balance.TotalWalletBalance);

        return PortfolioState.Create(
            exchangeAccountId,
            positions,
            capital,
            calculatedAt,
            staleAfter);
    }
}
