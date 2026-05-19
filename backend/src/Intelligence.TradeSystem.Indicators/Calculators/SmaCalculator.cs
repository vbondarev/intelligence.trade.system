using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет метод для расчёта SMA (Simple Moving Average).
/// </summary>
/// <remarks>
/// В данной реализации SMA вычисляется по последним <c>period</c> значениям массива.
/// Если доступных значений меньше указанного периода, усредняются все доступные значения.
/// </remarks>
public static class SmaCalculator
{
    /// <summary>
    /// Вычисляет простую скользящую среднюю (SMA) и возвращает структурированный результат <see cref="IndicatorValue"/>.
    /// </summary>
    /// <param name="values">Последовательность значений, например цен закрытия.</param>
    /// <param name="period">Период расчёта SMA. Должен быть больше нуля.</param>
    /// <returns>
    /// <see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.EmptyInput"/>, если массив пуст.
    /// <see cref="IndicatorValue.Fallback"/> с причиной <see cref="IndicatorValueReason.PartialWindow"/>, если данных меньше периода.
    /// <see cref="IndicatorValue.Available"/> при полноценном расчёте по <paramref name="period"/> значениям.
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

        return values.Length < period
            ? IndicatorValue.Fallback(values.Average(), IndicatorValueReason.PartialWindow)
            : IndicatorValue.Available(values[^period..].Average());
    }
}
