namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Предоставляет методы для расчёта RSI (Relative Strength Index).
/// </summary>
/// <remarks>
/// В данной реализации используется классический RSI по методу Уайлдера:
/// сначала рассчитываются положительные и отрицательные изменения цены закрытия,
/// затем начальные средние значения инициализируются как SMA первых <c>period</c> изменений,
/// после чего применяется сглаживание Уайлдера.
/// <para>
/// Если данных недостаточно для полного расчёта, метод возвращает <c>50</c>,
/// что соответствует нейтральному значению RSI.
/// </para>
/// </remarks>
public static class RsiCalculator
{
    /// <summary>
    /// Вычисляет значение RSI в диапазоне от 0 до 100.
    /// </summary>
    /// <param name="closes">Последовательность цен закрытия.</param>
    /// <param name="period">
    /// Период расчёта RSI. Должен быть больше нуля.
    /// По умолчанию используется значение <c>14</c>.
    /// </param>
    /// <returns>
    /// Значение RSI в диапазоне <c>[0; 100]</c>.
    /// Возвращает <c>50</c>, если данных недостаточно для расчёта.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="closes"/> равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    public static decimal Compute(decimal[] closes, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(closes);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (closes.Length < period + 1)
        {
            return 50m;
        }

        var gains = new decimal[closes.Length - 1];
        var losses = new decimal[closes.Length - 1];

        for (var i = 1; i < closes.Length; i++)
        {
            var delta = closes[i] - closes[i - 1];
            gains[i - 1] = delta > 0m ? delta : 0m;
            losses[i - 1] = delta < 0m ? -delta : 0m;
        }

        var avgGain = gains[..period].Average();
        var avgLoss = losses[..period].Average();

        for (var i = period; i < gains.Length; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0m)
        {
            return avgGain == 0m ? 50m : 100m;
        }

        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }
}
