namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Порт сохранения событий, которые должны фиксироваться вместе с породившим их бизнес-состоянием.
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
