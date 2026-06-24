using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Abstractions;

/// <summary>
/// Нейтральный контракт доступа к приватным данным торгового аккаунта биржи.
/// </summary>
public interface IPrivateAccountProvider
{
    /// <summary>
    /// Возвращает список открытых позиций торгового аккаунта.
    /// </summary>
    /// <param name="category">
    /// Категория рынка. Поддерживаются только категории, где биржа предоставляет позиции.
    /// </param>
    /// <param name="symbol">
    /// Тикер инструмента. Если <c>null</c>, реализация может вернуть все позиции выбранной категории.
    /// </param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Список доменных моделей <see cref="OpenPosition"/> с ненулевым размером;
    /// пустой список (<c>[]</c>) если запрос завершился ошибкой или позиций нет.
    /// </returns>
    /// <remarks>
    /// Приватный эндпоинт — обычно требует корректно настроенных API-ключей.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Если <paramref name="category"/> указывает на рынок, где позиции недоступны
    /// (например, <see cref="MarketCategory.Spot"/>).
    /// </exception>
    Task<IReadOnlyList<OpenPosition>> GetOpenPositionsAsync(
        MarketCategory category,
        string? symbol = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает баланс торгового аккаунта.
    /// </summary>
    /// <param name="accountType">Тип аккаунта, поддерживаемый целевой биржей.</param>
    /// <param name="cancellationToken">Токен отмены асинхронной операции.</param>
    /// <returns>
    /// Доменную модель <see cref="AccountBalance"/> с агрегированными суммами и списком монет,
    /// либо <c>null</c> если запрос завершился ошибкой.
    /// </returns>
    /// <remarks>
    /// Приватный эндпоинт — обычно требует корректно настроенных API-ключей.
    /// </remarks>
    Task<AccountBalance?> GetWalletBalanceAsync(
        AccountType accountType,
        CancellationToken cancellationToken = default);
}
