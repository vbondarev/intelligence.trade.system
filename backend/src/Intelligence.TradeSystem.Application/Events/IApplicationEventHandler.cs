using System.Diagnostics.CodeAnalysis;

namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Handles one typed application event.
/// </summary>
/// <remarks>
/// Handlers must be idempotent by <see cref="IApplicationEvent.EventId"/>. The dispatcher marks
/// an outbox row processed only after all registered handlers complete successfully.
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
