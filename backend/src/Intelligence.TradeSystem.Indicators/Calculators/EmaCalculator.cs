namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Вычисляет экспоненциальную скользящую среднюю (Exponential Moving Average).
/// Инициализируется через SMA первых <c>period</c> значений,
/// затем применяется сглаживание с коэффициентом <c>k = 2 / (period + 1)</c>.
/// </summary>
internal static class EmaCalculator
{
    /// <summary>
    /// Возвращает значение EMA на последнем элементе массива.
    /// Если данных меньше <paramref name="period"/>, возвращает SMA всех доступных значений.
    /// </summary>
    public static decimal Compute(decimal[] values, int period)
    {
        if (values.Length == 0) return 0m;
        if (values.Length < period) return values.Average();

        var k = 2m / (period + 1);

        // Seed: SMA первых period свечей
        var ema = values[..period].Average();

        for (var i = period; i < values.Length; i++)
            ema = values[i] * k + ema * (1m - k);

        return ema;
    }
}

