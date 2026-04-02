using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Indicators;

/// <summary>
/// Собирает <see cref="LongShortRatioSnapshot"/> из исторического ряда точек
/// соотношения лонг/шорт позиций <see cref="LongShortRatioEntry"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного списка</item>
///   <item>Сортировка по убыванию времени; определение текущих значений (последняя точка)</item>
///   <item>Вычисление средних BuyRatio / SellRatio по всему окну</item>
///   <item>Определение флагов доминирования и экстремальных значений</item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class LongShortRatioSnapshotAssembler
{
    /// <summary>
    /// Порог доли лонгов, при превышении которого позиционирование считается экстремальным.
    /// <c>CurrentBuyRatio &gt; ExtremeLongThreshold</c> → экстремально длинный рынок.
    /// <c>CurrentBuyRatio &lt; (1 − ExtremeLongThreshold)</c> → экстремально короткий рынок.
    /// Контрарный сигнал: экстремальные значения повышают вероятность разворота.
    /// </summary>
    internal const decimal ExtremeLongThreshold = 0.65m;

    /// <summary>
    /// Вычисляет и возвращает <see cref="LongShortRatioSnapshot"/> для переданного ряда точек.
    /// </summary>
    /// <param name="entries">Исторический ряд точек соотношения лонг/шорт позиций.</param>
    /// <param name="period">Период агрегации, с которым запрашивались данные.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="entries"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если список точек пустой.</exception>
    public static LongShortRatioSnapshot Assemble(
        IReadOnlyList<LongShortRatioEntry> entries,
        LongShortRatioPeriod period)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            throw new ArgumentException("Long/short ratio entries list must not be empty.", nameof(entries));

        // 2. Sort descending (newest first); current = first
        var sorted  = entries.OrderByDescending(e => e.Timestamp).ToList();
        var current = sorted[0];

        // 3. Averages over entire window
        var avgBuyRatio  = sorted.Average(e => e.BuyRatio);
        var avgSellRatio = sorted.Average(e => e.SellRatio);

        // 4. Flags
        var isLongDominant  = current.BuyRatio > 0.5m;
        var isExtremelyLong  = current.BuyRatio >  ExtremeLongThreshold;
        var isExtremelyShort = current.BuyRatio < (1m - ExtremeLongThreshold);

        // 5. Assemble
        return new LongShortRatioSnapshot
        {
            Symbol   = current.Symbol,
            Category = current.Category,
            Period   = period,

            WindowStartUtc = sorted[^1].Timestamp,
            WindowEndUtc   = current.Timestamp,

            CurrentBuyRatio  = current.BuyRatio,
            CurrentSellRatio = current.SellRatio,
            AvgBuyRatio      = avgBuyRatio,
            AvgSellRatio     = avgSellRatio,

            IsLongDominant  = isLongDominant,
            IsExtremelyLong  = isExtremelyLong,
            IsExtremelyShort = isExtremelyShort,
        };
    }
}

