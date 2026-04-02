namespace Intelligence.TradeSystem.Indicators.Calculators;

/// <summary>
/// Вычисляет средний истинный диапазон (Average True Range) по методу Уайлдера.
/// True Range = max(H−L, |H−PrevClose|, |L−PrevClose|).
/// Инициализируется через SMA первых <c>period</c> значений TR.
/// </summary>
internal static class AtrCalculator
{
    /// <summary>
    /// Возвращает значение ATR на последней свече.
    /// Возвращает <c>0</c>, если данных меньше двух свечей.
    /// </summary>
    public static decimal Compute(decimal[] highs, decimal[] lows, decimal[] closes, int period = 14)
    {
        var count = Math.Min(highs.Length, Math.Min(lows.Length, closes.Length));
        if (count < 2) return 0m;

        var trueRanges = new decimal[count - 1];

        for (var i = 1; i < count; i++)
        {
            var hl = highs[i] - lows[i];
            var hc = Math.Abs(highs[i] - closes[i - 1]);
            var lc = Math.Abs(lows[i]  - closes[i - 1]);
            trueRanges[i - 1] = Math.Max(hl, Math.Max(hc, lc));
        }

        if (trueRanges.Length < period) return trueRanges.Average();

        // Seed: SMA первых period значений TR
        var atr = trueRanges[..period].Average();

        // Сглаживание Уайлдера
        for (var i = period; i < trueRanges.Length; i++)
            atr = (atr * (period - 1) + trueRanges[i]) / period;

        return atr;
    }
}

