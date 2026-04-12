namespace Intelligence.TradeSystem.Api.Contracts;

/// <summary>
/// Базовый HTTP response contract для проверки liveness/readiness API-хоста.
/// </summary>
public sealed record HealthResponse
{
    /// <summary>Имя сервиса, который отвечает на health-запрос.</summary>
    public required string Service { get; init; }

    /// <summary>Текущий статус health-проверки.</summary>
    public required string Status { get; init; }
}

