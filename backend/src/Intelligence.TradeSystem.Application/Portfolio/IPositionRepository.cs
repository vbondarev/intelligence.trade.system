using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

public interface IPositionRepository
{
    Task<Versioned<Position>?> GetByIdAsync(
        PositionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно сохраняет позицию вместе с новыми записями в append-only истории.
    /// </summary>
    /// <param name="position">Позиция для сохранения.</param>
    /// <param name="expectedVersion">
    /// Версия, под которой был прочитан агрегат перед изменением, или <c>null</c>, если
    /// вызывающий код ожидает вставку новой строки (конфликт, если строка уже существует).
    /// </param>
    /// <returns>Версия, под которой агрегат теперь сохранён.</returns>
    /// <remarks>
    /// Версия позиции проверяется и обновляется в той же транзакции, что и добавление
    /// новых <see cref="Domain.History.PositionChange"/>: если CAS-проверка версии
    /// проигрывает, ни одна новая запись истории не сохраняется.
    /// </remarks>
    /// <exception cref="ConcurrencyConflictException">
    /// Ожидаемая версия не совпала с фактической, либо строка уже существует при вставке,
    /// либо строка была удалена другим писателем.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        Position position,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);
}
