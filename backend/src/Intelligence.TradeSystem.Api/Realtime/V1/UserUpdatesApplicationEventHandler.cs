using Intelligence.TradeSystem.Application.Events;
using Microsoft.AspNetCore.SignalR;

namespace Intelligence.TradeSystem.Api.Realtime.V1;

internal sealed class UserUpdatesApplicationEventHandler(
    IHubContext<UpdatesHub> hubContext) :
    IApplicationEventHandler<PositionOpenedEventV1>,
    IApplicationEventHandler<PositionChangedEventV1>,
    IApplicationEventHandler<PositionClosedEventV1>,
    IApplicationEventHandler<ExchangeAccountUpdatedEventV1>,
    IApplicationEventHandler<ExchangeAccountSyncDegradedEventV1>,
    IApplicationEventHandler<PortfolioUpdatedEventV1>,
    IApplicationEventHandler<PositionEvaluationUpdatedEventV1>
{
    public Task HandleAsync(
        PositionOpenedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        SendPositionUpdatedAsync(
            applicationEvent.UserId,
            applicationEvent.EventId,
            applicationEvent.OccurredAt,
            applicationEvent.PositionId,
            cancellationToken);

    public Task HandleAsync(
        PositionChangedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        SendPositionUpdatedAsync(
            applicationEvent.UserId,
            applicationEvent.EventId,
            applicationEvent.OccurredAt,
            applicationEvent.PositionId,
            cancellationToken);

    public Task HandleAsync(
        PositionClosedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        SendPositionUpdatedAsync(
            applicationEvent.UserId,
            applicationEvent.EventId,
            applicationEvent.OccurredAt,
            applicationEvent.PositionId,
            cancellationToken);

    public Task HandleAsync(
        ExchangeAccountUpdatedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        SendAccountUpdatedAsync(
            applicationEvent.UserId,
            applicationEvent.EventId,
            applicationEvent.OccurredAt,
            applicationEvent.ExchangeAccountId,
            cancellationToken);

    public Task HandleAsync(
        ExchangeAccountSyncDegradedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        SendAccountUpdatedAsync(
            applicationEvent.UserId,
            applicationEvent.EventId,
            applicationEvent.OccurredAt,
            applicationEvent.ExchangeAccountId,
            cancellationToken);

    public Task HandleAsync(
        PortfolioUpdatedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .User(FormatUserId(applicationEvent.UserId))
            .SendAsync(
                RealtimeEventNames.PortfolioUpdated,
                new PortfolioUpdatedMessageV1(
                    applicationEvent.EventId,
                    applicationEvent.OccurredAt,
                    applicationEvent.ExchangeAccountId),
                cancellationToken);

    public Task HandleAsync(
        PositionEvaluationUpdatedEventV1 applicationEvent,
        CancellationToken cancellationToken = default) =>
        hubContext.Clients
            .User(FormatUserId(applicationEvent.UserId))
            .SendAsync(
                RealtimeEventNames.EvaluationUpdated,
                new EvaluationUpdatedMessageV1(
                    applicationEvent.EventId,
                    applicationEvent.OccurredAt,
                    applicationEvent.PositionId),
                cancellationToken);

    private Task SendPositionUpdatedAsync(
        Guid userId,
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid positionId,
        CancellationToken cancellationToken) =>
        hubContext.Clients
            .User(FormatUserId(userId))
            .SendAsync(
                RealtimeEventNames.PositionUpdated,
                new PositionUpdatedMessageV1(eventId, occurredAt, positionId),
                cancellationToken);

    private Task SendAccountUpdatedAsync(
        Guid userId,
        Guid eventId,
        DateTimeOffset occurredAt,
        Guid exchangeAccountId,
        CancellationToken cancellationToken) =>
        hubContext.Clients
            .User(FormatUserId(userId))
            .SendAsync(
                RealtimeEventNames.ExchangeAccountUpdated,
                new ExchangeAccountUpdatedMessageV1(
                    eventId,
                    occurredAt,
                    exchangeAccountId),
                cancellationToken);

    private static string FormatUserId(Guid userId) =>
        userId == Guid.Empty
            ? throw new ArgumentException("Application event UserId must be initialized.", nameof(userId))
            : userId.ToString("D");
}
