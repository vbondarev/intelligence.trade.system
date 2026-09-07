using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountRepository
{
    Task<Versioned<ExchangeAccount>?> GetByIdAsync(
        UserId userId,
        ExchangeAccountId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет аккаунт в указанном user scope с CAS-проверкой оптимистической конкурентности.
    /// </summary>
    /// <param name="userId">Владелец прикладной операции.</param>
    /// <param name="account">Аккаунт для сохранения.</param>
    /// <param name="expectedVersion">
    /// Версия, под которой был прочитан агрегат перед изменением, или <c>null</c>, если
    /// вызывающий код ожидает вставку новой строки (конфликт, если строка уже существует).
    /// </param>
    /// <returns>Версия, под которой агрегат теперь сохранён.</returns>
    /// <exception cref="ConcurrencyConflictException">
    /// Ожидаемая версия не совпала с фактической, либо строка уже существует при вставке,
    /// либо агрегат недоступен в указанном user scope. Эта ошибка не различает отсутствующий
    /// и чужой ресурс.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        ExchangeAccount account,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);
}
