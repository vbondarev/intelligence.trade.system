using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts.Credentials;

/// <summary>
/// Граница хранения пар учётных данных биржи в области пользователя.
/// </summary>
public interface IExchangeAccountCredentialStore
{
    /// <summary>
    /// Читает учётные данные, принадлежащие <paramref name="userId"/>. Отсутствующие и чужие строки
    /// дают одинаковый наблюдаемый результат.
    /// </summary>
    Task<ExchangeAccountCredential?> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает только технические метаданные сохранённой пары, не расшифровывая её.
    /// Отсутствующие и чужие строки дают одинаковый наблюдаемый результат.
    /// </summary>
    Task<ExchangeAccountCredentialMetadata?> GetMetadataAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт учётные данные для существующей учётной записи, не заменяя имеющуюся пару.
    /// </summary>
    Task<ConcurrencyVersion> CreateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ExchangeAccountCredentialSecret secret,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Заменяет полную пару учётных данных с помощью compare-and-swap.
    /// </summary>
    Task<ConcurrencyVersion> RotateAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        ExchangeAccountCredentialSecret replacement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет локально сохранённую пару с помощью compare-and-swap. Это не отзывает
    /// соответствующий ключ на бирже.
    /// </summary>
    Task RevokeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Повторно шифрует ту же пару текущим активным ключом защиты с помощью
    /// compare-and-swap.
    /// </summary>
    Task<ConcurrencyVersion> ReprotectAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);
}
