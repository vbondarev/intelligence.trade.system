using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

public interface IRecommendationRepository
{
    Task<Versioned<Recommendation>?> GetByIdAsync(
        RecommendationId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет рекомендацию с CAS-проверкой оптимистической конкурентности.
    /// </summary>
    /// <param name="recommendation">Рекомендация для сохранения.</param>
    /// <param name="expectedVersion">
    /// Версия, под которой был прочитан агрегат перед изменением, или <c>null</c>, если
    /// вызывающий код ожидает вставку новой строки (конфликт, если строка уже существует).
    /// </param>
    /// <returns>Версия, под которой агрегат теперь сохранён.</returns>
    /// <exception cref="ConcurrencyConflictException">
    /// Ожидаемая версия не совпала с фактической, либо строка уже существует при вставке,
    /// либо строка была удалена другим писателем.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        Recommendation recommendation,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);
}
