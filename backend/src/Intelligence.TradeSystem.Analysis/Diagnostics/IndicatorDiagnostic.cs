using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Analysis.Diagnostics;

/// <summary>
/// Описывает проблему качества расчёта одного scalar-индикатора:
/// fallback-значение или недоступность.
/// </summary>
/// <remarks>
/// Создаётся только когда <see cref="IndicatorValueExtensions.ShouldReportDiagnostic"/> возвращает <c>true</c>.
/// Используется для формирования warnings/diagnostics в LLM payload и snapshot health.
/// </remarks>
public sealed record IndicatorDiagnostic
{
    /// <summary>
    /// Таймфрейм, для которого рассчитывался индикатор.
    /// Возможные значения: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.
    /// </summary>
    public required string Timeframe { get; init; }

    /// <summary>
    /// Идентификатор индикатора: <c>ema20</c>, <c>ema50</c>, <c>ema200</c>,
    /// <c>rsi14</c>, <c>atr14</c>, <c>volumeSma20</c>, <c>volumeRatio</c>.
    /// </summary>
    public required string Indicator { get; init; }

    /// <summary>Причина, по которой значение является fallback или недоступным.</summary>
    public required IndicatorValueReason Reason { get; init; }

    /// <summary>
    /// <c>true</c>, если значение рассчитано по fallback-логике (данные есть, но окно неполное).
    /// <c>false</c>, если значение недоступно полностью.
    /// </summary>
    public required bool IsFallback { get; init; }

    /// <summary>
    /// Человекочитаемое сообщение, пригодное для логов, warnings и LLM payload.
    /// Формат: <c>{timeframe}.{indicator} calculated using fallback: {reason}.</c>
    /// или <c>{timeframe}.{indicator} unavailable: {reason}.</c>
    /// </summary>
    public required string Message { get; init; }
}
