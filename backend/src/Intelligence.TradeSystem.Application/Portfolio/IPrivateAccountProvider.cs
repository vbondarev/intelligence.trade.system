using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Нейтральный контракт доступа к приватным данным торгового аккаунта биржи.
/// </summary>
public interface IPrivateAccountProvider
{
    Task<IReadOnlyList<OpenPosition>> GetOpenPositionsAsync(
        MarketCategory category,
        string? symbol = null,
        CancellationToken cancellationToken = default);

    Task<AccountBalance?> GetWalletBalanceAsync(
        AccountType accountType,
        CancellationToken cancellationToken = default);
}
