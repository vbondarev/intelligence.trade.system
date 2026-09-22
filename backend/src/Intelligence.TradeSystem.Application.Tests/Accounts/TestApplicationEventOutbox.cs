using Intelligence.TradeSystem.Application.Events;

namespace Intelligence.TradeSystem.Application.Tests.Accounts;

internal sealed class TestApplicationEventOutbox : IApplicationEventOutbox
{
    public List<IApplicationEvent> Events { get; } = [];

    public Task AddAsync(
        IApplicationEvent applicationEvent,
        CancellationToken cancellationToken = default)
    {
        Events.Add(applicationEvent);
        return Task.CompletedTask;
    }

    public Task AddRangeAsync(
        IReadOnlyCollection<IApplicationEvent> applicationEvents,
        CancellationToken cancellationToken = default)
    {
        Events.AddRange(applicationEvents);
        return Task.CompletedTask;
    }
}
