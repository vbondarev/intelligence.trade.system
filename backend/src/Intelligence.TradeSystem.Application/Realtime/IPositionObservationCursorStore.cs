using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Хранилище per-position курсоров частичных потоковых наблюдений в области пользователя.
/// </summary>
/// <remarks>
/// Отсутствующий, чужой или удалённый аккаунт дают одинаковый наблюдаемый результат.
/// Операции участвуют в текущей транзакции базы данных, поэтому курсор сохраняется атомарно
/// вместе с бизнес-состоянием и записями outbox этой транзакции.
/// </remarks>
public interface IPositionObservationCursorStore
{
    Task<Versioned<PositionObservationCursor>?> GetAsync(
        UserId userId,
        PositionObservationCursorKey key,
        CancellationToken cancellationToken = default);

    /// <returns>Курсоры аккаунта, упорядоченные по категории, символу и <c>positionIdx</c>.</returns>
    Task<IReadOnlyCollection<Versioned<PositionObservationCursor>>> GetByExchangeAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Создаёт (<paramref name="expectedVersion"/> равен <see langword="null"/>) или обновляет курсор
    /// с помощью compare-and-swap от имени поколения <paramref name="expectedGeneration"/>.
    /// </summary>
    /// <remarks>
    /// Поколение проверяется под разделяемой блокировкой строки состояния наблюдения, которая
    /// удерживается до завершения текущей транзакции: смена поколения не может завершиться между
    /// проверкой и фиксацией. Известные компоненты ревизии курсора не уменьшаются и не сбрасываются.
    /// </remarks>
    /// <exception cref="ArgumentException">У курсора нет ни одного компонента ревизии.</exception>
    /// <exception cref="ConcurrencyConflictException">
    /// Курсор уже существует или изменён, ревизия регрессирует, поколение не текущее либо аккаунт
    /// недоступен в области пользователя. Владелец workflow перечитывает состояние и решает заново.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        PositionObservationCursor cursor,
        ConcurrencyVersion? expectedVersion,
        long expectedGeneration,
        CancellationToken cancellationToken = default);
}
