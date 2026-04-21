namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Ответ API для проверки доступности и готовности сервиса.
/// </summary>
public sealed record HealthResponse
{
    /// <summary>Имя сервиса, сформировавшего ответ.</summary>
    public required string Service { get; init; }

    /// <summary>Текущий итоговый статус проверки состояния сервиса.</summary>
    public required string Status { get; init; }
}

