using Intelligence.TradeSystem.MarketIntelligence.Indicators.Results;

namespace Intelligence.TradeSystem.MarketIntelligence.Indicators.Calculators;

/// <summary>
/// Предоставляет метод для расчёта RSI (Relative Strength Index).
/// </summary>
/// <remarks>
/// В данной реализации используется классический RSI по методу Уайлдера:
/// сначала рассчитываются положительные и отрицательные изменения цены закрытия,
/// затем начальные средние значения инициализируются как SMA первых <c>period</c> изменений,
/// после чего применяется сглаживание Уайлдера.
/// <para>
/// Если данных недостаточно для полного расчёта (<c>closes.Length &lt; period + 1</c>),
/// метод возвращает <see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.InsufficientData"/>.
/// RSI не использует fallback при нехватке данных.
/// </para>
/// </remarks>
public static class RsiCalculator
{
    /// <summary>
    /// Вычисляет RSI и возвращает структурированный результат <see cref="IndicatorValue"/>.
    /// </summary>
    /// <param name="closes">Последовательность цен закрытия.</param>
    /// <param name="period">
    /// Период расчёта RSI. Должен быть больше нуля.
    /// По умолчанию используется значение <c>14</c>.
    /// </param>
    /// <returns>
    /// <see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.EmptyInput"/>, если массив пуст.
    /// <see cref="IndicatorValue.Unavailable"/> с причиной <see cref="IndicatorValueReason.InsufficientData"/>, если данных меньше <c>period + 1</c>.
    /// <see cref="IndicatorValue.Available"/> со значением <c>50m</c> для flat market (avgGain == 0 и avgLoss == 0).
    /// <see cref="IndicatorValue.Available"/> со значением <c>100m</c> при только росте (avgLoss == 0, avgGain &gt; 0).
    /// <see cref="IndicatorValue.Available"/> со значением <c>0m</c> при только падении (avgGain == 0, avgLoss &gt; 0).
    /// <see cref="IndicatorValue.Available"/> с RSI-значением при полноценном расчёте.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Выбрасывается, если <paramref name="closes"/> равен <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="period"/> меньше или равен нулю.
    /// </exception>
    public static IndicatorValue Compute(decimal[] closes, int period = 14)
    {
        ArgumentNullException.ThrowIfNull(closes);

        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be greater than zero.");
        }

        if (closes.Length == 0)
        {
            return IndicatorValue.Unavailable(IndicatorValueReason.EmptyInput);
        }

        if (closes.Length < period + 1)
        {
            return IndicatorValue.Unavailable(IndicatorValueReason.InsufficientData);
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
            return IndicatorValue.Available(avgGain == 0m ? 50m : 100m);
        }

        var rs = avgGain / avgLoss;
        return IndicatorValue.Available(100m - 100m / (1m + rs));
    }
}
