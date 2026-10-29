using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Realtime.Observations;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Infrastructure.Realtime;

internal static partial class ExchangeRealtimeObservationLogMessages
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Фоновое realtime-наблюдение биржевых аккаунтов отключено конфигурацией.")]
    public static partial void LogDisabled(ILogger logger);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "Начат поиск аккаунтов для realtime-наблюдения.")]
    public static partial void LogDiscoveryStarted(ILogger logger);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Error,
        Message = "Не удалось получить кандидатов realtime-наблюдения. ExceptionType={ExceptionType}")]
    public static partial void LogDiscoveryFailed(ILogger logger, string? exceptionType);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Warning,
        Message = "Курсор поиска кандидатов realtime-наблюдения не продвинулся.")]
    public static partial void LogCursorDidNotAdvance(ILogger logger);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Error,
        Message = "Realtime-наблюдатель аккаунта не запущен. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, ExceptionType={ExceptionType}")]
    public static partial void LogProviderStartFailed(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1205,
        Level = LogLevel.Information,
        Message = "Realtime-наблюдатель аккаунта запущен. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, Generation={Generation}")]
    public static partial void LogProviderStarted(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        long generation);

    [LoggerMessage(
        EventId = 1206,
        Level = LogLevel.Information,
        Message = "Realtime-наблюдатель аккаунта остановлен. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}")]
    public static partial void LogProviderStopped(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId);

    [LoggerMessage(
        EventId = 1213,
        Level = LogLevel.Error,
        Message = "Не удалось освободить realtime-провайдер аккаунта. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, ExceptionType={ExceptionType}")]
    public static partial void LogProviderDisposeFailed(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1207,
        Level = LogLevel.Warning,
        Message = "Не удалось обновить состояние realtime-наблюдения. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, ExceptionType={ExceptionType}")]
    public static partial void LogStateUpdateFailed(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1208,
        Level = LogLevel.Warning,
        Message = "Realtime-наблюдение аккаунта требует REST recovery. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, TransportStatus={TransportStatus}")]
    public static partial void LogRecoveryRequired(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        RealtimeTransportStatus transportStatus);

    [LoggerMessage(
        EventId = 1214,
        Level = LogLevel.Warning,
        Message = "Стартовая REST-сверка realtime-наблюдения не завершена полностью. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, SyncOutcome={SyncOutcome}")]
    public static partial void LogInitialReconciliationIncomplete(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        ExchangeAccountSyncOutcome syncOutcome);

    [LoggerMessage(
        EventId = 1209,
        Level = LogLevel.Warning,
        Message = "События realtime-наблюдения не приняты ограниченной очередью. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}")]
    public static partial void LogQueueOverflow(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId);

    [LoggerMessage(
        EventId = 1210,
        Level = LogLevel.Error,
        Message = "Realtime-обработка аккаунта завершилась ошибкой. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}, ExceptionType={ExceptionType}")]
    public static partial void LogAccountProcessingFailed(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId,
        string? exceptionType);

    [LoggerMessage(
        EventId = 1211,
        Level = LogLevel.Information,
        Message = "Фоновое realtime-наблюдение останавливается.")]
    public static partial void LogStopping(ILogger logger);

    [LoggerMessage(
        EventId = 1212,
        Level = LogLevel.Warning,
        Message = "Наблюдатель остановлен, так как аккаунт больше не подключён. UserId={UserId}, ExchangeAccountId={ExchangeAccountId}")]
    public static partial void LogAccountNoLongerConnected(
        ILogger logger,
        Guid userId,
        Guid exchangeAccountId);
}
