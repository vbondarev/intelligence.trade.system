using System.Diagnostics.CodeAnalysis;

namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Обрабатывает одно типизированное прикладное событие.
/// </summary>
/// <remarks>
/// Обработчики должны быть идемпотентными по <see cref="IApplicationEvent.EventId"/>. Диспетчер помечает
/// запись outbox как обработанную только после успешного завершения всех зарегистрированных обработчиков.
/// Обработчики жизненного цикла позиции должны дополнительно отклонять событие позиции, если его
/// <c>PositionId + PositionChangeSequence</c> не новее последней принятой ими последовательности.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "The EventHandler suffix is part of the application event contract naming.")]
public interface IApplicationEventHandler<in TEvent>
    where TEvent : IApplicationEvent
{
    Task HandleAsync(TEvent applicationEvent, CancellationToken cancellationToken = default);
}
