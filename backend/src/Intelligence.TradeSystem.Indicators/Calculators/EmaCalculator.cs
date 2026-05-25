using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет метод для расчёта EMA (Exponential Moving Average).
/// </summary>
/// <remarks>
/// В данной реализации EMA инициализируется через SMA первых <c>period</c> значений,
/// после чего рассчитывается по классической формуле сглаживания:
/// <code>
/// EMA = Price * k + PreviousEMA * (1 - k)
/// k = 2 / (period + 1)
/// </code>
/// Если данных меньше периода, возвращается среднее по всем доступным значениям.
/// </remarks>
public static class EmaCalculator
{
    /// <summary>
    /// Вычисляет EMA и возвращает структурированный результат <see cref="IndicatorValue"/>.
    /// </summary>
    /// <param name="values">Последовательность значений, например цен закрытия.</param>
    /// <param name="period">Период EMA. Должен быть больше нуля.</param>
    /// <returns>
    /// <see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.EmptyInput"/>, если массив пуст.
    /// <see cref="IndicatorValue.Fallback"/> с причиной <see cref="IndicatorValueReason.PartialWindow"/>, если данных меньше периода.
    /// <see cref="IndicatorValue.Available"/> при seed-расчёте (count == period) или полноценном EMA-расчёте (count &gt; period).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="values"/> равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    public static IndicatorValue Compute(decimal[] values, int period)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (values.Length == 0)
        {
            return IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);
        }

        if (values.Length < period)
        {
            return IndicatorValue.Fallback(values.Average(), IndicatorValueReason.PartialWindow);
        }

        var k = 2m / (period + 1m);
        var ema = values[..period].Average();

        for (var i = period; i < values.Length; i++)
        {
            ema = values[i] * k + ema * (1m - k);
        }

        return IndicatorValue.Available(ema);
    }
}
