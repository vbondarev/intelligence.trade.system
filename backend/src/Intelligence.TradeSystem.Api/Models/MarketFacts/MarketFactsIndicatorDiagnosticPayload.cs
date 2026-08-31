namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Диагностическая запись об индикаторе конкретного таймфрейма.
/// </summary>
public sealed record MarketFactsIndicatorDiagnosticPayload
{
    /// <summary>Таймфрейм. Например: <c>15m</c>, <c>1h</c>.</summary>
    public required string Timeframe { get; init; }

    /// <summary>Название индикатора. Например: <c>RSI14</c>, <c>EMA200</c>.</summary>
    public required string Indicator { get; init; }

    /// <summary>Причина проблемы. Например: <c>insufficient_data</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>Признак того, что использовано fallback-значение.</summary>
    public required bool IsFallback { get; init; }

    /// <summary>Человекочитаемое сообщение о проблеме.</summary>
    public required string Message { get; init; }
}
