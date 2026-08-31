namespace Intelligence.TradeSystem.MarketIntelligence.Snapshots;

/// <summary>
/// Диагностическая запись для одного индикатора одного таймфрейма.
/// Сигнализирует о том, что индикатор был рассчитан по fallback-логике
/// или недоступен из-за нехватки данных.
/// </summary>
public sealed record IndicatorDiagnosticSnapshot
{
    /// <summary>Обозначение таймфрейма, например <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</summary>
    public required string Timeframe { get; init; }

    /// <summary>Имя индикатора, например <c>rsi14</c>, <c>ema200</c>, <c>atr14</c>.</summary>
    public required string Indicator { get; init; }

    /// <summary>
    /// Строковое представление причины: <c>InsufficientData</c>, <c>PartialWindow</c>, <c>EmptyInput</c>, <c>InvalidInput</c>.
    /// Использует строку вместо enum, чтобы не вводить зависимость Domain → Indicators.
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
