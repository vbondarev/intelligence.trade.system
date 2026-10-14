using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Хранилище состояния приватного realtime-наблюдения в области пользователя: по одному
/// состоянию на биржевой аккаунт.
/// </summary>
/// <remarks>
/// Отсутствующий, чужой или удалённый аккаунт дают одинаковый наблюдаемый результат.
/// Операции участвуют в текущей транзакции базы данных, если она открыта.
/// </remarks>
public interface IRealtimeObservationStateStore
{
    /// <returns>Состояние и его версия; <see langword="null"/>, если состояние не сохранялось.</returns>
    Task<Versioned<RealtimeObservationState>?> GetAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно начинает новое поколение наблюдателя: увеличивает <see cref="RealtimeObservationState.Generation"/>
    /// (первое поколение — 1), устанавливает <paramref name="status"/>, требует recovery, сбрасывает
    /// heartbeat и код причины. <see cref="RealtimeObservationState.LastConnectedAt"/> и
    /// <see cref="RealtimeObservationState.LastFullyReconciledAt"/> сохраняются.
    /// </summary>
    /// <remarks>
    /// Используется при запуске наблюдателя, отключении аккаунта и смене credentials: после этого
    /// запись состояния и курсоров от прежнего поколения отклоняется.
    /// </remarks>
    /// <exception cref="ConcurrencyConflictException">Аккаунт недоступен в области пользователя.</exception>
    Task<Versioned<RealtimeObservationState>> AdvanceGenerationAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        RealtimeObservationTransportStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Сохраняет состояние текущего поколения с помощью compare-and-swap. Поколение этой операцией
    /// не меняется.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">
    /// Версия или поколение изменились, либо аккаунт недоступен в области пользователя.
    /// </exception>
    Task<ConcurrencyVersion> SaveAsync(
        UserId userId,
        RealtimeObservationState state,
        ConcurrencyVersion expectedVersion,
        CancellationToken cancellationToken = default);
}
