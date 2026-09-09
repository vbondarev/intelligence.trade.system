using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

public class ExchangeAccountBackgroundSyncWorker(
    IExchangeAccountBackgroundSyncSweep sweep,
    IOptions<ExchangeAccountBackgroundSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<ExchangeAccountBackgroundSyncWorker> logger)
    : BackgroundService
{
    private readonly ExchangeAccountBackgroundSyncOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Enabled)
        {
            ExchangeAccountBackgroundSyncLogMessages.LogDisabled(logger);
            return;
        }

        var plannedStart = timeProvider.GetUtcNow() + settings.InitialDelay;

        try
        {
            await DelayUntilAsync(plannedStart, stoppingToken).ConfigureAwait(false);

            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();
                var actualStart = timeProvider.GetUtcNow();
                var schedulerLag = ExchangeAccountBackgroundSyncSchedule.CalculateLag(
                    plannedStart,
                    actualStart);
                using var activity =
                    ExchangeAccountBackgroundSyncTelemetry.StartSweepActivity(schedulerLag);
                ExchangeAccountBackgroundSyncTelemetry.RecordSweepStarted(schedulerLag);
                ExchangeAccountBackgroundSyncLogMessages.LogSweepStarted(
                    logger,
                    plannedStart,
                    actualStart,
                    schedulerLag.TotalSeconds);

                var timestamp = timeProvider.GetTimestamp();
                ExchangeAccountBackgroundSyncSweepResult? result = null;
                try
                {
                    result = await sweep.RunAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ExchangeAccountBackgroundSyncLogMessages.LogSweepFailed(
                        logger,
                        exception.GetType().FullName);
                }

                var duration = timeProvider.GetElapsedTime(timestamp);
                if (result is not null)
                {
                    ExchangeAccountBackgroundSyncTelemetry.RecordSweepCompleted(duration, result);
                    activity?.SetTag("candidate.count", result.CandidateCount);
                    activity?.SetTag("processed.count", result.ProcessedCount);
                    activity?.SetTag(
                        "unexpected_failure.count",
                        result.UnexpectedFailureCount);
                    ExchangeAccountBackgroundSyncLogMessages.LogSweepCompleted(
                        logger,
                        duration.TotalSeconds,
                        result.CandidateCount,
                        result.ProcessedCount,
                        result.UnexpectedFailureCount,
                        result.CandidateLoadFailed);
                }

                plannedStart = ExchangeAccountBackgroundSyncSchedule.CalculateNextPlannedStart(
                    plannedStart,
                    timeProvider.GetUtcNow(),
                    settings.Interval);
                await DelayUntilAsync(plannedStart, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            ExchangeAccountBackgroundSyncLogMessages.LogStopping(logger);
        }
    }

    private Task DelayUntilAsync(
        DateTimeOffset plannedStart,
        CancellationToken cancellationToken)
    {
        var delay = plannedStart - timeProvider.GetUtcNow();
        return delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, timeProvider, cancellationToken);
    }
}
