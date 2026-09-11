namespace Intelligence.TradeSystem.Application.Events;

/// <summary>
/// Долговечный контракт прикладного события.
/// </summary>
/// <remarks>
/// Доставка выполняется не менее одного раза. Потребители должны использовать <see cref="EventId"/> для идемпотентности
/// поскольку процесс может остановиться после успешного выполнения обработчика, но до того, как запись outbox будет отмечена как
/// обработанная.
/// </remarks>
public interface IApplicationEvent
{
    Guid EventId { get; }

    string EventType { get; }

    int SchemaVersion { get; }

    DateTimeOffset OccurredAt { get; }
}
