namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>
/// LLM-оптимизированное представление диагностической записи индикатора.
/// Объясняет, почему значение индикатора равно <c>null</c> или рассчитано по fallback-логике.
/// </summary>
public sealed record LlmIndicatorDiagnosticPayload
{
    /// <summary>Обозначение таймфрейма, например <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</summary>
    public required string Timeframe { get; init; }

    /// <summary>Имя индикатора, например <c>rsi14</c>, <c>ema200</c>, <c>atr14</c>.</summary>
    public required string Indicator { get; init; }

    /// <summary>
    /// Причина: <c>InsufficientData</c>, <c>PartialWindow</c>, <c>EmptyInput</c>, <c>InvalidInput</c>.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// <c>true</c> — индикатор рассчитан по fallback-логике (значение доступно, но менее точно);
    /// <c>false</c> — индикатор недоступен.
    /// </summary>
    public required bool IsFallback { get; init; }

    /// <summary>Человекочитаемое сообщение, описывающее проблему.</summary>
    public required string Message { get; init; }
}

