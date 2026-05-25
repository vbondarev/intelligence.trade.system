using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Analysis.Diagnostics;

/// <summary>
/// Extension methods для удобного добавления <see cref="IndicatorDiagnostic"/> в список
/// без boilerplate-кода в assembler-ах.
/// </summary>
public static class IndicatorDiagnosticListExtensions
{
    /// <summary>
    /// Создаёт diagnostic для указанного <paramref name="value"/> и добавляет его в коллекцию,
    /// если <see cref="IndicatorValueExtensions.ShouldReportDiagnostic"/> возвращает <c>true</c>.
    /// </summary>
    /// <param name="diagnostics">Целевая коллекция. Не может быть <see langword="null"/>.</param>
    /// <param name="timeframe">Таймфрейм: <c>15m</c>, <c>1h</c>, <c>4h</c>, <c>1d</c>.</param>
    /// <param name="indicator">Имя индикатора: <c>ema20</c>, <c>rsi14</c> и т. д.</param>
    /// <param name="value">Результат расчёта индикатора. Не может быть <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="diagnostics"/> или <paramref name="value"/> равны <see langword="null"/>.
    /// </exception>
    public static void AddIfNeeded(
        this ICollection<IndicatorDiagnostic> diagnostics,
        string timeframe,
        string indicator,
        IndicatorValue value)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(value);

        var diagnostic = IndicatorDiagnosticFactory.Create(timeframe, indicator, value);

        if (diagnostic is not null)
        {
            diagnostics.Add(diagnostic);
        }
    }
}

