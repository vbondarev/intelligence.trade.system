namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Вычисляет индекс относительной силы (Relative Strength Index) по методу Уайлдера.
/// Инициализируется через SMA первых <c>period</c> изменений,
/// затем применяется сглаживание <c>1 / period</c>.
/// </summary>
internal static class RsiCalculator
{
    /// <summary>
    /// Возвращает значение RSI в диапазоне [0, 100].
    /// Возвращает <c>50</c>, если данных недостаточно для расчёта.
    /// </summary>
    public static decimal Compute(decimal[] closes, int period = 14)
    {
        if (closes.Length < period + 1) return 50m;

        var gains  = new decimal[closes.Length - 1];
        var losses = new decimal[closes.Length - 1];

        for (var i = 1; i < closes.Length; i++)
        {
            var delta = closes[i] - closes[i - 1];
            gains[i - 1]  = delta > 0m ?  delta : 0m;
            losses[i - 1] = delta < 0m ? -delta : 0m;
        }

        // Seed: SMA первых period изменений
        var avgGain = gains[..period].Average();
        var avgLoss = losses[..period].Average();

        // Сглаживание Уайлдера
        for (var i = period; i < gains.Length; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i])  / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0m) return avgGain == 0m ? 50m : 100m;

        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }
}
