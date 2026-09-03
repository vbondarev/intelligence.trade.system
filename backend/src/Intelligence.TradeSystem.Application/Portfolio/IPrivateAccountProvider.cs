using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Нейтральный контракт доступа к приватным данным торгового аккаунта биржи.
/// </summary>
public interface IPrivateAccountProvider
{
    /// <summary>
    /// Запрашивает открытые позиции для заданной области (scope). Результат явно различает
    /// успешный снимок без позиций от неудачной попытки получить данные — см.
    /// <see cref="OpenPositionsObservation"/>.
    /// </summary>
    Task<OpenPositionsObservation> GetOpenPositionsAsync(
        MarketCategory category,
        string? symbol = null,
        CancellationToken cancellationToken = default);

    Task<AccountBalance?> GetWalletBalanceAsync(
        AccountType accountType,
        CancellationToken cancellationToken = default);
}
