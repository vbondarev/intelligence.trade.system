namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Вычисляет простую скользящую среднюю (Simple Moving Average) за последние <c>period</c> значений.
/// </summary>
internal static class SmaCalculator
{
    /// <summary>
    /// Возвращает SMA последних <paramref name="period"/> элементов массива.
    /// Если элементов меньше <paramref name="period"/>, усредняются все доступные.
    /// </summary>
    public static decimal Compute(decimal[] values, int period)
    {
        if (values.Length == 0) return 0m;

        var take = Math.Min(period, values.Length);
        return values[^take..].Average();
    }
}

