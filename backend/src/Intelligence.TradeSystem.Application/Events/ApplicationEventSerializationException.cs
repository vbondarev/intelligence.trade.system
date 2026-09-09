namespace Intelligence.TradeSystem.Application.Events;

public sealed class ApplicationEventSerializationException : Exception
{
    public ApplicationEventSerializationException(
        string message,
        string? eventType = null,
        int? schemaVersion = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        EventType = eventType;
        SchemaVersion = schemaVersion;
    }

    public string? EventType { get; }

    public int? SchemaVersion { get; }
}
