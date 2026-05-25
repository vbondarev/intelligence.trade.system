using Intelligence.TradeSystem.Indicators.Results;

namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет метод для расчёта индикатора ATR (Average True Range).
/// </summary>
/// <remarks>
/// ATR — это индикатор волатильности, показывающий средний истинный диапазон цены
/// за выбранный период.
/// <para>
/// В данной реализации используется классический подход Уайлдера:
/// сначала рассчитывается последовательность <c>True Range</c>, затем:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>для начального значения ATR используется среднее первых <c>period</c> значений TR;</description>
/// </item>
/// <item>
/// <description>последующие значения сглаживаются по формуле Уайлдера.</description>
/// </item>
/// </list>
/// <para>
/// Если данных недостаточно для полного периода, метод возвращает <see cref="IndicatorValue.Fallback"/> со средним по доступным
/// значениям <c>True Range</c>.
/// </para>
/// </remarks>
public static class AtrCalculator
{
    /// <summary>
    /// Вычисляет ATR (Average True Range) и возвращает структурированный результат <see cref="IndicatorValue"/>.
    /// </summary>
    /// <param name="highs">Массив максимальных цен свечей.</param>
    /// <param name="lows">Массив минимальных цен свечей.</param>
    /// <param name="closes">Массив цен закрытия свечей.</param>
    /// <param name="period">
    /// Период расчёта ATR. Должен быть больше нуля. По умолчанию <c>14</c>.
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description><see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.InsufficientData"/> при количестве свечей меньше двух.</description></item>
    /// <item><description><see cref="IndicatorValue.Fallback"/> с причиной <see cref="IndicatorValueReason.PartialWindow"/> при количестве True Range меньше <paramref name="period"/>; значение равно среднему по доступным True Range.</description></item>
    /// <item><description><see cref="IndicatorValue.Available"/> при полноценном ATR по формуле Уайлдера.</description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если один из массивов равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Выбрасывается, если длины массивов <paramref name="highs"/>, <paramref name="lows"/>
    /// и <paramref name="closes"/> не совпадают.
    /// <para>
    /// Метод намеренно применяет fail-fast политику: рассинхронизация OHLC-массивов
    /// означает баг в pipeline сборки данных и не должна скрываться молчаливой обрезкой.
    /// Входные массивы обязаны иметь одинаковую длину — они представляют поля одних и тех же свечей.
    /// </para>
    /// </exception>
    public static IndicatorValue Compute(decimal[] highs, decimal[] lows, decimal[] closes, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(highs);
        ArgumentNullException.ThrowIfNull(lows);
        ArgumentNullException.ThrowIfNull(closes);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (highs.Length != lows.Length || highs.Length != closes.Length)
        {
            throw new ArgumentException("Highs, lows and closes arrays must have the same length.");
        }

        var count = highs.Length;

        if (count < 2)
        {
            return IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);
        }

        var trueRanges = new decimal[count - 1];

        for (var i = 1; i < count; i++)
        {
            var highLow = Math.Abs(highs[i] - lows[i]);
            var highPrevClose = Math.Abs(highs[i] - closes[i - 1]);
            var lowPrevClose = Math.Abs(lows[i] - closes[i - 1]);

            trueRanges[i - 1] = Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose));
        }

        if (trueRanges.Length < period)
        {
            return IndicatorValue.Fallback(trueRanges.Average(), IndicatorValueReason.PartialWindow);
        }

        // Seed: SMA первых period значений TR
        var atr = trueRanges.Take(period).Average();

        // Сглаживание Уайлдера
        for (var i = period; i < trueRanges.Length; i++)
        {
            atr = ((atr * (period - 1)) + trueRanges[i]) / period;
        }

        return IndicatorValue.Available(atr);
    }
}
