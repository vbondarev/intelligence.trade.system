using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;

namespace Intelligence.TradeSystem.Analysis.Diagnostics;

/// <summary>
/// Создаёт <see cref="IndicatorDiagnostic"/> из <see cref="IndicatorValue"/>.
/// Возвращает <c>null</c> для полноценных available значений без fallback.
/// </summary>
public static class IndicatorDiagnosticFactory
{
    /// <summary>
    /// Создаёт diagnostic из результата расчёта индикатора, или возвращает <c>null</c>,
    /// если diagnostic не требуется.
    /// </summary>
    /// <param name="timeframe">Таймфрейм: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</param>
    /// <param name="indicator">Имя индикатора: <c>ema20</c>, <c>rsi14</c> и т. д.</param>
    /// <param name="value">Результат расчёта индикатора.</param>
    /// <returns>
    /// <see cref="IndicatorDiagnostic"/>, если <see cref="IndicatorValueExtensions.ShouldReportDiagnostic"/>
    /// возвращает <c>true</c>; иначе <c>null</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="value"/> равен <see langword="null"/>.
    /// </exception>
    public static IndicatorDiagnostic? Create(
        string timeframe,
        string indicator,
        IndicatorValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!value.ShouldReportDiagnostic())
        {
            return null;
        }

        var message = value.IsFallback
            ? $"{timeframe}.{indicator} calculated using fallback: {value.Reason}."
            : $"{timeframe}.{indicator} unavailable: {value.Reason}.";

        return new IndicatorDiagnostic
        {
            Timeframe = timeframe,
            Indicator = indicator,
            Reason = value.Reason,
            IsFallback = value.IsFallback,
            Message = message,
        };
    }
}
