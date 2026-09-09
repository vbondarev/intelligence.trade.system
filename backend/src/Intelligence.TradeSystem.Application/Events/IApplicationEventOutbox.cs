namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Persistence port for events that must commit with the business state that produced them.
/// </summary>
public interface IApplicationEventOutbox
{
    Task AddAsync(
        IApplicationEvent applicationEvent,
        CancellationToken cancellationToken = default);

    Task AddRangeAsync(
        IReadOnlyCollection<IApplicationEvent> applicationEvents,
        CancellationToken cancellationToken = default);
}
