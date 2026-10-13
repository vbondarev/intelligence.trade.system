using Intelligence.TradeSystem.Application.Realtime.Observations;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Приватный потоковый провайдер одного биржевого аккаунта (позиции и кошелёк), только чтение.
/// </summary>
/// <remarks>
/// Экземпляр одноразовый: создаётся для конкретного аккаунта и учётных данных, запускается один
/// раз и освобождается через <see cref="IAsyncDisposable.DisposeAsync"/>, которое закрывает
/// подписки и соединение. После завершения освобождения наблюдения не доставляются: оно дожидается
/// уже выполняющихся вызовов получателя, поэтому получатель не должен синхронно ожидать освобождение.
/// </remarks>
public interface IExchangeRealtimeProvider : IAsyncDisposable
{
    ExchangeAccountId ExchangeAccountId { get; }

    /// <summary>
    /// Подписывается на приватные потоки позиций и кошелька.
    /// </summary>
    /// <returns>
    /// <see cref="RealtimeTransportStatus.Subscribed"/>, если подтверждены все подписки; иначе
    /// <see cref="RealtimeTransportStatus.SubscribeFailed"/> с нормализованной ошибкой, при этом
    /// успевшие подписки закрываются.
    /// </returns>
    /// <exception cref="InvalidOperationException">Провайдер уже запускался или освобождён.</exception>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<RealtimeTransportState> StartAsync(
        IExchangeRealtimeObserver observer,
        CancellationToken cancellationToken = default);
}
