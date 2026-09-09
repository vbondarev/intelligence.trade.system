using Intelligence.TradeSystem.Application.Accounts;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Infrastructure.BackgroundSynchronization;

internal static partial class ExchangeAccountBackgroundSyncLogMessages
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Exchange account background synchronization is disabled by configuration.")]
    public static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Information,
        Message = "Exchange account background sync sweep started. " +
                  "PlannedStart={PlannedStart}, ActualStart={ActualStart}, " +
                  "SchedulerLagSeconds={SchedulerLagSeconds}")]
    public static partial void LogSweepStarted(
        ILogger logger,
        DateTimeOffset plannedStart,
        DateTimeOffset actualStart,
        double schedulerLagSeconds);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Error,
        Message = "Exchange account background sync sweep failed unexpectedly. " +
                  "ExceptionType={ExceptionType}")]
    public static partial void LogSweepFailed(ILogger logger, string? exceptionType);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Information,
        Message = "Exchange account background sync sweep completed. " +
                  "DurationSeconds={DurationSeconds}, CandidateCount={CandidateCount}, " +
                  "ProcessedCount={ProcessedCount}, UnexpectedFailureCount={UnexpectedFailureCount}, " +
                  "CandidateLoadFailed={CandidateLoadFailed}")]
    public static partial void LogSweepCompleted(
        ILogger logger,
        double durationSeconds,
        int candidateCount,
        int processedCount,
        int unexpectedFailureCount,
        bool candidateLoadFailed);

    [LoggerMessage(
        EventId = 1104,
        Level = LogLevel.Information,
        Message = "Exchange account background synchronization is stopping.")]
    public static partial void LogStopping(ILogger logger);

    [LoggerMessage(
        EventId = 1105,
        Level = LogLevel.Error,
        Message = "Exchange account background sync candidate load failed. " +
                  "ExceptionType={ExceptionType}")]
    public static partial void LogCandidateLoadFailed(ILogger logger, string? exceptionType);

    [LoggerMessage(
        EventId = 1106,
        Level = LogLevel.Warning,
        Message = "Exchange account background sync candidate source did not advance its cursor.")]
    public static partial void LogCursorDidNotAdvance(ILogger logger);

    [LoggerMessage(
        EventId = 1107,
        Level = LogLevel.Error,
        Message = "Exchange account background sync failed unexpectedly. " +
                  "UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, " +
                  "ExceptionType={ExceptionType}")]
    public static partial void LogAccountFailed(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1108,
        Level = LogLevel.Information,
        Message = "Exchange account background sync completed. Outcome={Outcome}, " +
                  "UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, " +
                  "LastSuccessfulSyncAt={LastSuccessfulSyncAt}, " +
                  "LastSuccessfulSyncAgeSeconds={LastSuccessfulSyncAgeSeconds}")]
    public static partial void LogAccountCompleted(
        ILogger logger,
        ExchangeAccountSyncOutcome outcome,
        Guid userId,
        Guid exchangeAccountId,
        DateTimeOffset? lastSuccessfulSyncAt,
        double? lastSuccessfulSyncAgeSeconds);

    [LoggerMessage(
        EventId = 1109,
        Level = LogLevel.Warning,
        Message = "Exchange account background sync completed in a degraded state. " +
                  "Outcome={Outcome}, UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, " +
                  "LastSuccessfulSyncAt={LastSuccessfulSyncAt}, " +
                  "LastSuccessfulSyncAgeSeconds={LastSuccessfulSyncAgeSeconds}")]
    public static partial void LogAccountDegraded(
        ILogger logger,
        ExchangeAccountSyncOutcome outcome,
        Guid userId,
        Guid exchangeAccountId,
        DateTimeOffset? lastSuccessfulSyncAt,
        double? lastSuccessfulSyncAgeSeconds);

    [LoggerMessage(
        EventId = 1110,
        Level = LogLevel.Warning,
        Message = "Exchange account background sync returned an unrecognized outcome. " +
                  "Outcome={Outcome}, UserId={UserId}, ExchangeAccountId={ExchangeAccountId}")]
    public static partial void LogUnknownOutcome(
        ILogger logger,
        ExchangeAccountSyncOutcome outcome,
        Guid userId,
        Guid exchangeAccountId);
}
