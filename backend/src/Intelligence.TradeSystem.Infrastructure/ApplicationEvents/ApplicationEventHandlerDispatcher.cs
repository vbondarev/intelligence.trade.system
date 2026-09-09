using Intelligence.TradeSystem.Application.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

internal static class ApplicationEventHandlerDispatcher
{
    public static Task<bool> DispatchAsync(
        IServiceProvider serviceProvider,
        IApplicationEvent applicationEvent,
        CancellationToken cancellationToken) =>
        applicationEvent switch
        {
            PositionOpenedEventV1 value => DispatchTypedAsync(
                serviceProvider,
                value,
                cancellationToken),
            PositionChangedEventV1 value => DispatchTypedAsync(
                serviceProvider,
                value,
                cancellationToken),
            PositionClosedEventV1 value => DispatchTypedAsync(
                serviceProvider,
                value,
                cancellationToken),
            ExchangeAccountSyncDegradedEventV1 value => DispatchTypedAsync(
                serviceProvider,
                value,
                cancellationToken),
            _ => throw new InvalidOperationException(
                $"No dispatcher mapping exists for application event '{applicationEvent.EventType}'."),
        };

    private static async Task<bool> DispatchTypedAsync<TEvent>(
        IServiceProvider serviceProvider,
        TEvent applicationEvent,
        CancellationToken cancellationToken)
        where TEvent : IApplicationEvent
    {
        var handlers = serviceProvider
            .GetServices<IApplicationEventHandler<TEvent>>()
            .ToArray();
        if (handlers.Length == 0)
            return false;

        foreach (var handler in handlers)
        {
            await handler
                .HandleAsync(applicationEvent, cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }
}
