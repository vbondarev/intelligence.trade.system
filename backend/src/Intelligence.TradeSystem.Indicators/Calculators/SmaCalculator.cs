namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет методы для расчёта SMA (Simple Moving Average).
/// </summary>
/// <remarks>
/// В данной реализации SMA вычисляется по последним <c>period</c> значениям массива.
/// Если доступных значений меньше указанного периода, усредняются все доступные значения.
/// </remarks>
internal static class SmaCalculator
{
    /// <summary>
    /// Вычисляет простую скользящую среднюю (SMA) по последним значениям массива.
    /// </summary>
    /// <param name="values">Последовательность значений, например цен закрытия.</param>
    /// <param name="period">
    /// Период расчёта SMA. Должен быть больше нуля.
    /// </param>
    /// <returns>
    /// Значение SMA.
    /// Возвращает <c>0</c>, если массив пуст.
    /// Если данных меньше периода, возвращает среднее по всем доступным значениям.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="values"/> равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    public static decimal Compute(decimal[] values, int period)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (values.Length == 0)
        {
            return 0m;
        }

        var take = Math.Min(period, values.Length);
        return values[^take..].Average();
    }
}
