using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Account-owned курсор последнего принятого биржевого <c>MessageCreatedAt</c> wallet-наблюдения.
/// </summary>
/// <remarks>
/// Курсор изолирован по <see cref="UserId"/> и <see cref="ExchangeAccountId"/>, переживает restart и
/// смену экземпляра и не зависит от <c>ReceivedAt</c> или нормализованного backend
/// <c>ObservedAt</c>. Отсутствие записи означает неизвестный биржевой порядок (<c>unknown</c>), а не
/// фиктивное время. Операции участвуют в текущей транзакции и сохраняются атомарно с принятым
/// капиталом, состоянием аккаунта и transactional outbox.
/// </remarks>
public interface IWalletObservationCursorStore
{
    /// <returns>
    /// Последний принятый биржевой <c>MessageCreatedAt</c>; <see langword="null"/>, если курсор ещё
    /// не установлен.
    /// </returns>
    Task<DateTimeOffset?> GetLastAcceptedMessageCreatedAtAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Записывает принятый биржевой <c>MessageCreatedAt</c>. Вызывается только после успешного
    /// применения капитала; значение канонизируется до точности PostgreSQL.
    /// </summary>
    Task SaveAcceptedAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset messageCreatedAt,
        CancellationToken cancellationToken = default);
}
