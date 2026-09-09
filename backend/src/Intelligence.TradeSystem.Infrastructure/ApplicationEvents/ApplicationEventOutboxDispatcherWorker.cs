using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

public sealed class ApplicationEventOutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ApplicationEventOutboxDispatcherOptions> options,
    TimeProvider timeProvider,
    ILogger<ApplicationEventOutboxDispatcherWorker> logger)
    : BackgroundService
{
    private readonly ApplicationEventOutboxDispatcherOptions settings = options.Value;
    private readonly string instanceId =
        $"{Environment.MachineName}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            ApplicationEventOutboxLogMessages.LogDisabled(logger);
            return;
        }

        try
        {
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var now = timeProvider.GetUtcNow();
                IReadOnlyList<OutboxMessageClaim> claims;
                try
                {
                    claims = await ClaimAsync(now, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ApplicationEventOutboxLogMessages.LogClaimFailed(
                        logger,
                        exception.GetType().FullName);
                    await DelayAsync(stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (claims.Count > 0)
                {
                    ApplicationEventOutboxTelemetry.RecordClaimed(
                        claims.Count,
                        now,
                        claims.Min(claim => (DateTimeOffset?)claim.CreatedAt));
                    await DispatchBatchAsync(claims, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await DelayAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            ApplicationEventOutboxLogMessages.LogStopping(logger);
        }
    }

    private async Task<IReadOnlyList<OutboxMessageClaim>> ClaimAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
        return await store
            .ClaimAsync(
                settings.BatchSize,
                instanceId,
                now,
                settings.ClaimDuration,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task DispatchBatchAsync(
        IReadOnlyList<OutboxMessageClaim> claims,
        CancellationToken cancellationToken) =>
        Parallel.ForEachAsync(
            claims,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = settings.MaxConcurrency,
            },
            async (claim, token) =>
            {
                if (token.IsCancellationRequested)
                    return;

                await DispatchOneAsync(claim, token).ConfigureAwait(false);
            });

    private async Task DispatchOneAsync(
        OutboxMessageClaim claim,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();
        using var activity = ApplicationEventOutboxTelemetry.StartDispatchActivity(claim.EventType);
        try
        {
            var applicationEvent = ApplicationEventSerializer.Deserialize(
                claim.EventType,
                claim.SchemaVersion,
                claim.Payload);

            await using (var handlerScope = scopeFactory.CreateAsyncScope())
            {
                var hasHandlers = await ApplicationEventHandlerDispatcher
                    .DispatchAsync(
                        handlerScope.ServiceProvider,
                        applicationEvent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!hasHandlers)
                {
                    ApplicationEventOutboxLogMessages.LogNoHandlers(
                        logger,
                        claim.EventId,
                        claim.EventType,
                        claim.AttemptCount);
                    throw new InvalidOperationException(
                        $"No handlers are registered for application event '{claim.EventType}'.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            await using var persistenceScope = scopeFactory.CreateAsyncScope();
            var store = persistenceScope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
            var markedProcessed = await store
                .MarkProcessedAsync(
                    claim,
                    timeProvider.GetUtcNow(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (markedProcessed)
            {
                ApplicationEventOutboxLogMessages.LogDelivered(
                    logger,
                    claim.EventId,
                    claim.EventType);
                ApplicationEventOutboxTelemetry.RecordProcessed(claim.EventType);
                ApplicationEventOutboxTelemetry.RecordDispatchDuration(
                    claim.EventType,
                    timeProvider.GetElapsedTime(startedAt),
                    "processed");
            }
            else
            {
                ApplicationEventOutboxTelemetry.RecordDispatchDuration(
                    claim.EventType,
                    timeProvider.GetElapsedTime(startedAt),
                    "claim_lost");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("outcome", "cancelled");
            ApplicationEventOutboxTelemetry.RecordDispatchDuration(
                claim.EventType,
                timeProvider.GetElapsedTime(startedAt),
                "cancelled");
        }
        catch (Exception exception)
        {
            ApplicationEventOutboxLogMessages.LogDeliveryFailed(
                logger,
                claim.EventId,
                claim.EventType,
                claim.AttemptCount,
                exception.GetType().FullName);
            ApplicationEventOutboxTelemetry.RecordFailed(claim.EventType);

            var nextAttemptAt = timeProvider.GetUtcNow() +
                CalculateRetryDelay(settings.RetryBaseDelay, claim.AttemptCount);
            try
            {
                await using var retryScope = scopeFactory.CreateAsyncScope();
                var store = retryScope.ServiceProvider.GetRequiredService<IOutboxMessageStore>();
                var scheduled = await store
                    .ScheduleRetryAsync(claim, nextAttemptAt, CancellationToken.None)
                    .ConfigureAwait(false);
                if (scheduled)
                {
                    ApplicationEventOutboxTelemetry.RecordRetry(claim.EventType);
                }
            }
            catch (Exception retryException)
            {
                ApplicationEventOutboxLogMessages.LogRetryScheduleFailed(
                    logger,
                    claim.EventId,
                    claim.EventType,
                    claim.AttemptCount,
                    retryException.GetType().FullName);
            }

            ApplicationEventOutboxTelemetry.RecordDispatchDuration(
                claim.EventType,
                timeProvider.GetElapsedTime(startedAt),
                "failed");
        }
    }

    private Task DelayAsync(CancellationToken cancellationToken) =>
        Task.Delay(settings.PollingInterval, timeProvider, cancellationToken);

    private static TimeSpan CalculateRetryDelay(
        TimeSpan baseDelay,
        int attemptCount)
    {
        var exponent = Math.Min(attemptCount, 10);
        var multiplier = 1L << exponent;
        var ticks = baseDelay.Ticks > TimeSpan.MaxValue.Ticks / multiplier
            ? TimeSpan.FromHours(1).Ticks
            : baseDelay.Ticks * multiplier;
        return TimeSpan.FromTicks(Math.Min(ticks, TimeSpan.FromHours(1).Ticks));
    }
}
