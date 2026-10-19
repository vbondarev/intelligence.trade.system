using Intelligence.TradeSystem.Application.Realtime.Ordering;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Account-owned история REST-интервалов для межэкземплярного согласования порядка REST/WebSocket.
/// </summary>
/// <remarks>
/// История принадлежит аккаунту и не требует строки <c>RealtimeObservationState</c>. Операции
/// участвуют в текущей транзакции базы данных, если она открыта. Неуспешные и отклонённые
/// наблюдения не должны вызывать запись.
/// </remarks>
public interface IExchangeRestOrderingEvidenceStore
{
    /// <summary>
    /// Добавляет интервал успешного REST-чтения. Границы должны удовлетворять
    /// <c>StartedAt &lt;= CompletedAt</c>; иначе метод завершается исключением.
    /// </summary>
    Task AppendAsync(
        UserId userId,
        ExchangeRestOrderingInterval interval,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает интервалы позиций аккаунта, которые могут пересечься с
    /// <paramref name="observedAt"/> или биржевым <paramref name="exchangeFactAt"/> для указанного
    /// символа: полные окна и scoped-окна символа. Непересекающиеся интервалы не сливаются.
    /// </summary>
    Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetPositionEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string symbol,
        DateTimeOffset observedAt,
        DateTimeOffset? exchangeFactAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Возвращает интервалы баланса аккаунта, которые могут пересечься с временными метками
    /// wallet-наблюдения.
    /// </summary>
    Task<IReadOnlyList<ExchangeRestOrderingInterval>> GetBalanceEvidenceAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset? observedAt,
        DateTimeOffset? messageCreatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True, если есть Full/Scoped evidence символа, связанное с актуальным принятым REST-состоянием
    /// (<paramref name="acceptedRestObservationAt"/> = <c>StartedAt</c> интервала) и завершённое
    /// строго раньше <paramref name="streamObservedAt"/>. Исторический интервал другой сверки таким
    /// доказательством не является; пустой результат <see cref="GetPositionEvidenceAsync"/> — тоже.
    /// </summary>
    Task<bool> HasAcceptedPositionRestEvidenceCompletedBeforeAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        string symbol,
        DateTimeOffset acceptedRestObservationAt,
        DateTimeOffset streamObservedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True, если есть Balance evidence с <c>CompletedAt</c>, равным
    /// <paramref name="completedAt"/> (после каноникализации до микросекунд). Это связывает
    /// evidence с принятым REST-капиталом, у которого <c>ObservedAt</c> совпадает с завершением чтения.
    /// </summary>
    Task<bool> HasBalanceEvidenceCompletingAtAsync(
        UserId userId,
        ExchangeAccountId exchangeAccountId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);
}
