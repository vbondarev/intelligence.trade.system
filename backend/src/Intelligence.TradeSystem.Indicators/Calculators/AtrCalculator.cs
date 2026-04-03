namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет методы для расчёта индикатора ATR (Average True Range).
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
/// Если данных недостаточно для полного периода, метод возвращает среднее по доступным
/// значениям <c>True Range</c>.
/// </para>
/// </remarks>
internal static class AtrCalculator
{
    /// <summary>
    /// Вычисляет ATR (Average True Range) по последовательностям High, Low и Close.
    /// </summary>
    /// <param name="highs">Массив максимальных цен свечей.</param>
    /// <param name="lows">Массив минимальных цен свечей.</param>
    /// <param name="closes">Массив цен закрытия свечей.</param>
    /// <param name="period">
    /// Период расчёта ATR. Должен быть больше нуля.
    /// По умолчанию используется значение <c>14</c>.
    /// </param>
    /// <returns>
    /// Значение ATR.
    /// <para>
    /// Возвращает <c>0</c>, если данных меньше двух свечей.
    /// Если данных недостаточно для полного периода, возвращает среднее по доступным
    /// значениям <c>True Range</c>.
    /// </para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если один из массивов <paramref name="highs"/>, <paramref name="lows"/>
    /// или <paramref name="closes"/> равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    public static decimal Compute(decimal[] highs, decimal[] lows, decimal[] closes, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(highs);
        ArgumentNullException.ThrowIfNull(lows);
        ArgumentNullException.ThrowIfNull(closes);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        var count = Math.Min(highs.Length, Math.Min(lows.Length, closes.Length));

        if (count < 2)
        {
            return 0m;
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
            return trueRanges.Average();
        }

        // Seed: SMA первых period значений TR
        var atr = trueRanges.Take(period).Average();

        // Сглаживание Уайлдера
        for (var i = period; i < trueRanges.Length; i++)
        {
            atr = ((atr * (period - 1)) + trueRanges[i]) / period;
        }

        return atr;
    }
}
