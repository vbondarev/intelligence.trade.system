using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Accounts;

public interface IExchangeAccountRepository
{
    Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListActiveAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<Versioned<ExchangeAccount>?> GetByIdAsync(
        UserId userId,
        ExchangeAccountId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Находит подключение конкретного provider-side аккаунта биржи в области пользователя,
    /// включая отключённые (<see cref="ExchangeAccountConnectionStatus.Disabled"/>) подключения.
    /// </summary>
    /// <remarks>
    /// Lookup обслуживает внутренний lifecycle-инвариант
    /// <c>UserId + ExchangeId + ProviderIdentity → один ExchangeAccountId</c>; provider identity
    /// не является клиентским идентификатором.
    /// </remarks>
    Task<Versioned<ExchangeAccount>?> GetByProviderIdentityAsync(
        UserId userId,
        ExchangeId exchangeId,
        ExchangeAccountProviderIdentity providerIdentity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет учётную запись в указанной области пользователя с CAS-проверкой оптимистической конкурентности.
    /// </summary>
    /// <param name="userId">Владелец прикладной операции.</param>
    /// <param name="account">Учётная запись для сохранения.</param>
    /// <param name="expectedVersion">
    /// Версия, под которой был прочитан агрегат перед изменением, или <c>null</c>, если
    /// вызывающий код ожидает вставку новой строки (конфликт, если строка уже существует).
    /// </param>
    /// <returns>Версия, под которой агрегат теперь сохранён.</returns>
    /// <exception cref="ConcurrencyConflictException">
    /// Ожидаемая версия не совпала с фактической, либо строка уже существует при вставке,
    /// либо при вставке у пользователя уже есть подключение того же provider-side аккаунта биржи,
    /// либо агрегат недоступен в указанном user scope. Эта ошибка не различает отсутствующий
    /// и чужой ресурс.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        ExchangeAccount account,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет агрегат в указанном user scope с CAS-проверкой версии.
    /// </summary>
    Task DeleteAsync(
        UserId userId,
        ExchangeAccountId id,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);
}
