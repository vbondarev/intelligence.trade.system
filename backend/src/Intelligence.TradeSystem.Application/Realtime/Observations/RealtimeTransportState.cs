using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>
/// Изменение состояния потокового транспорта биржевого аккаунта.
/// </summary>
/// <param name="Status">Новое состояние.</param>
/// <param name="OccurredAt">Время фиксации состояния backend.</param>
/// <param name="Failure">Нормализованная ошибка биржи, если состояние вызвано ошибкой.</param>
public sealed record RealtimeTransportState(
    RealtimeTransportStatus Status,
    DateTimeOffset OccurredAt,
    ExchangeFailure? Failure = null)
{
    /// <summary>
    /// Требуется ли REST recovery: после этого состояния потоковые наблюдения не гарантируют полноту.
    /// </summary>
    public bool RecoveryRequired => Status is not RealtimeTransportStatus.Subscribed;
}
