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
        TimeSpan staleAfter,
        bool positionsFullyReconciled = true)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var capital = balance is null
            ? new PortfolioCapitalState(null, null, null)
            : CreateCapital(balance, balanceObservedAt);

        return AssembleWithCapital(
            capital,
            positions,
            exchangeAccountId,
            calculatedAt,
            staleAfter,
            positionsFullyReconciled);
    }

    /// <summary>
    /// Нормализует account-level значения баланса, включая нормализованный биржей
    /// нереализованный PnL аккаунта, который не выводится из position-level PnL.
    /// </summary>
    public static PortfolioCapitalState CreateCapital(AccountBalance balance, DateTimeOffset? observedAt)
    {
        ArgumentNullException.ThrowIfNull(balance);

        return new PortfolioCapitalState(
            balance.TotalEquity,
            balance.TotalAvailableBalance,
            observedAt,
            balance.TotalWalletBalance,
            balance.TotalPerpUnrealizedPnl);
    }

    public static PortfolioState AssembleWithCapital(
        PortfolioCapitalState capital,
        IReadOnlyCollection<Position> positions,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset calculatedAt,
        TimeSpan staleAfter,
        bool positionsFullyReconciled = true)
    {
        ArgumentNullException.ThrowIfNull(capital);
        ArgumentNullException.ThrowIfNull(positions);

        return PortfolioState.Create(
            exchangeAccountId,
            positions,
            capital,
            calculatedAt,
            staleAfter,
            positionsFullyReconciled);
    }
}
