using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="FundingRateSnapshot"/> из истории ставок финансирования
/// <see cref="FundingRateEntry"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного списка</item>
///   <item>Сортировка по убыванию времени; определение текущей ставки (последнее начисление)</item>
///   <item>Вычисление средних: Avg24h (последние 3 записи), Avg7d (последние 21 запись)</item>
///   <item>Вычисление Max / Min в окне</item>
///   <item>Определение флагов перегрева по порогу <see cref="ExtremeFundingThreshold"/></item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class FundingRateSnapshotAssembler
{
    /// <summary>
    /// Порог абсолютного значения ставки финансирования, при превышении которого
    /// рынок считается перегретым. Равен 10× стандартному значению Bybit (0.0001).
    /// <c>CurrentRate &gt; threshold</c> → бычий перегрев (лонги переплачивают).
    /// <c>CurrentRate &lt; -threshold</c> → медвежий перегрев (шорты переплачивают).
    /// </summary>
    private const decimal ExtremeFundingThreshold = AnalysisThresholds.FundingExtremeThreshold;

    /// <summary>Количество записей в 24 часах (8-часовой интервал начислений).</summary>
    private const int Periods24h = 3;

    /// <summary>Количество записей в 7 днях.</summary>
    private const int Periods7d = 21;

    /// <summary>
    /// Вычисляет и возвращает <see cref="FundingRateSnapshot"/> для переданной истории ставок.
    /// </summary>
    /// <param name="entries">История ставок финансирования с биржи.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="entries"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если список записей пустой.</exception>
    public static FundingRateSnapshot Assemble(IReadOnlyList<FundingRateEntry> entries)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            throw new ArgumentException("Funding rate entries list must not be empty.", nameof(entries));

        // 2. Sort descending (newest first); current = first
        var sorted = entries.OrderByDescending(e => e.Timestamp).ToList();
        var current = sorted[0];

        // 3. Averages
        var avg24h = sorted.Take(Periods24h).Average(e => e.FundingRate);
        var avg7d = sorted.Take(Periods7d).Average(e => e.FundingRate);

        // 4. Max / Min
        var max = sorted.Max(e => e.FundingRate);
        var min = sorted.Min(e => e.FundingRate);

        // 5. Flags
        var isPositive = current.FundingRate > 0m;
        var isExtremeBullish = current.FundingRate > ExtremeFundingThreshold;
        var isExtremeBearish = current.FundingRate < -ExtremeFundingThreshold;

        // 6. Assemble
        return new FundingRateSnapshot
        {
            Symbol = current.Symbol,
            Category = current.Category,

            WindowStartUtc = sorted[^1].Timestamp,
            WindowEndUtc = current.Timestamp,

            CurrentRate = current.FundingRate,
            Avg24hRate = avg24h,
            Avg7dRate = avg7d,
            MaxRate = max,
            MinRate = min,

            IsPositive = isPositive,
            IsExtremeBullish = isExtremeBullish,
            IsExtremeBearish = isExtremeBearish,
        };
    }
}
