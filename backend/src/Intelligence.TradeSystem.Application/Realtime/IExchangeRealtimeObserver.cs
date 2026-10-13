using Intelligence.TradeSystem.Application.Realtime.Observations;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Получатель нормализованных наблюдений потокового провайдера биржи.
/// </summary>
/// <remarks>
/// Методы вызываются из потоков транспорта биржи, в том числе конкурентно: сообщения и события
/// соединения доставляются разными потоками. Реализация должна быть потокобезопасной, не
/// блокировать вызывающий поток и не выполнять в нём прикладную обработку — например, передавать
/// наблюдения в ограниченную очередь последовательного consumer. Исключения реализации
/// перехватываются провайдером и не останавливают транспорт.
/// </remarks>
public interface IExchangeRealtimeObserver
{
    void OnPosition(PrivatePositionObservation observation);

    void OnWallet(PrivateWalletObservation observation);

    void OnTransportState(RealtimeTransportState state);
}
