using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

public interface IPositionRepository
{
    /// <summary>
    /// Загружает весь жизненный цикл одной биржевой учётной записи в указанной области пользователя,
    /// включая версию сохранения, необходимую для обновлений compare-and-swap.
    /// </summary>
    Task<IReadOnlyCollection<Versioned<Position>>> GetByExchangeAccountAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает лёгкий ID/version watermark текущих позиций биржевой учётной записи
    /// в указанной области пользователя, без materialization доменного агрегата и его
    /// <see cref="Domain.History.PositionChange"/> истории. Предназначен для pre-lock race
    /// detection, где полный агрегат не требуется.
    /// </summary>
    Task<IReadOnlyCollection<PositionVersionWatermark>> GetCurrentVersionWatermarksAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает текущие позиции аккаунта как лёгкие portfolio projections без истории lifecycle.
    /// </summary>
    Task<IReadOnlyCollection<PositionPortfolioProjection>> GetCurrentPortfolioPositionsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Загружает текущие агрегаты и историю только этих lifecycle в указанной области пользователя.
    /// </summary>
    Task<IReadOnlyCollection<Versioned<Position>>> GetCurrentWithHistoryAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает последнее закрытие каждого запрошенного физического слота в указанной области пользователя.
    /// </summary>
    Task<IReadOnlyCollection<LatestClosedPositionSlot>> GetLatestClosedSlotsAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        MarketCategory marketCategory,
        IReadOnlyCollection<PositionSlotKey> slots,
        CancellationToken cancellationToken = default);

    Task<Versioned<Position>?> GetByIdAsync(
        UserId userId,
        PositionId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно сохраняет позицию в указанном user scope вместе с новыми записями
    /// в append-only истории.
    /// </summary>
    /// <param name="userId">Владелец прикладной операции.</param>
    /// <param name="position">Позиция для сохранения.</param>
    /// <param name="expectedVersion">
    /// Версия, под которой был прочитан агрегат перед изменением, или <c>null</c>, если
    /// вызывающий код ожидает вставку новой строки (конфликт, если строка уже существует).
    /// </param>
    /// <returns>Версия, под которой агрегат теперь сохранён.</returns>
    /// <remarks>
    /// CAS-обновление версии позиции выполняется до чтения и проверки сохранённой
    /// истории и до добавления новых <see cref="Domain.History.PositionChange"/> в той
    /// же транзакции: если CAS-проверка версии проигрывает, ни одна новая запись
    /// истории не сохраняется.
    /// </remarks>
    /// <exception cref="ConcurrencyConflictException">
    /// Ожидаемая версия не совпала с фактической, либо строка уже существует при вставке,
    /// либо агрегат недоступен в указанном user scope. Эта ошибка не различает отсутствующий
    /// и чужой ресурс.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        Position position,
        ConcurrencyVersion? expectedVersion,
        CancellationToken cancellationToken = default);
}
