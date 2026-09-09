using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Infrastructure.ApplicationEvents;

internal static partial class ApplicationEventOutboxLogMessages
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Application event outbox dispatcher is disabled by configuration.")]
    public static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "Application event outbox dispatcher is stopping.")]
    public static partial void LogStopping(ILogger logger);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Error,
        Message = "Application event outbox claim failed. ExceptionType={ExceptionType}")]
    public static partial void LogClaimFailed(ILogger logger, string? exceptionType);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Warning,
        Message = "Application event has no registered handlers. EventId={EventId}, " +
                  "EventType={EventType}, AttemptCount={AttemptCount}")]
    public static partial void LogNoHandlers(
        ILogger logger,
        Guid eventId,
        string eventType,
        int attemptCount);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Error,
        Message = "Application event delivery failed. EventId={EventId}, " +
                  "EventType={EventType}, AttemptCount={AttemptCount}, ExceptionType={ExceptionType}")]
    public static partial void LogDeliveryFailed(
        ILogger logger,
        Guid eventId,
        string eventType,
        int attemptCount,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1206,
        Level = LogLevel.Error,
        Message = "Application event retry could not be scheduled. EventId={EventId}, " +
                  "EventType={EventType}, AttemptCount={AttemptCount}, ExceptionType={ExceptionType}")]
    public static partial void LogRetryScheduleFailed(
        ILogger logger,
        Guid eventId,
        string eventType,
        int attemptCount,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1205,
        Level = LogLevel.Debug,
        Message = "Application event delivered. EventId={EventId}, EventType={EventType}")]
    public static partial void LogDelivered(
        ILogger logger,
        Guid eventId,
        string eventType);
}
