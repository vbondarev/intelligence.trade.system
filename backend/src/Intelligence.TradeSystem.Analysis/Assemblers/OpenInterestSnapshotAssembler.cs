using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="OpenInterestSnapshot"/> из исторического ряда точек
/// открытого интереса <see cref="OpenInterestEntry"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного списка</item>
///   <item>Сортировка по времени; определение текущей точки (последней)</item>
///   <item>Поиск ближайших точек на горизонтах −1ч и −4ч для расчёта изменений</item>
///   <item>Вычисление пика и минимума в окне</item>
///   <item>Определение флагов аккумуляции / дистрибуции по порогу <see cref="TrendThresholdPct"/></item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class OpenInterestSnapshotAssembler
{
    /// <summary>
    /// Минимальное изменение OI за 1 час (в процентах), при превышении которого
    /// позиционирование считается аккумуляцией или дистрибуцией.
    /// </summary>
    internal const decimal TrendThresholdPct = 1m;

    /// <summary>
    /// Вычисляет и возвращает <see cref="OpenInterestSnapshot"/> для переданного ряда точек.
    /// </summary>
    /// <param name="entries">Исторический ряд точек открытого интереса.</param>
    /// <param name="interval">Интервал агрегации, с которым запрашивались данные.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="entries"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если список точек пустой.</exception>
    public static OpenInterestSnapshot Assemble(
        IReadOnlyList<OpenInterestEntry> entries,
        OpenInterestInterval interval)
    {
        // 1. Validate
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            throw new ArgumentException("Open interest entries list must not be empty.", nameof(entries));
        }

        // 2. Sort ascending; current = last (most recent)
        var sorted = entries.OrderBy(e => e.Timestamp).ToList();
        var current = sorted[^1];

        var symbol   = current.Symbol;
        var category = current.Category;

        // 3. Changes vs. 1h and 4h ago
        var change1hPct = ComputeChangePct(sorted, current, TimeSpan.FromHours(1));
        var change4hPct = ComputeChangePct(sorted, current, TimeSpan.FromHours(4));

        // 4. Peak / Trough
        var peak   = sorted.Max(e => e.OpenInterest);
        var trough = sorted.Min(e => e.OpenInterest);

        // 5. Trend flags
        var isAccumulating = change1hPct >  TrendThresholdPct;
        var isDistributing = change1hPct < -TrendThresholdPct;

        // 6. Assemble
        return new OpenInterestSnapshot
        {
            Symbol   = symbol,
            Category = category,
            Interval = interval,

            WindowStartUtc = sorted[0].Timestamp,
            WindowEndUtc   = current.Timestamp,

            CurrentOpenInterest = current.OpenInterest,
            PeakOpenInterest    = peak,
            TroughOpenInterest  = trough,

            Change1hPct = change1hPct,
            Change4hPct = change4hPct,

            IsAccumulating = isAccumulating,
            IsDistributing = isDistributing,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static decimal ComputeChangePct(
        List<OpenInterestEntry> sorted,
        OpenInterestEntry current,
        TimeSpan lookback)
    {
        var target = current.Timestamp - lookback;
        var past   = FindClosestTo(sorted, target);

        if (past is null || past.OpenInterest == 0m)
            return 0m;

        return Math.Round(
            (current.OpenInterest - past.OpenInterest) / past.OpenInterest * 100m,
            4);
    }

    private static OpenInterestEntry? FindClosestTo(
        List<OpenInterestEntry> sorted,
        DateTimeOffset target)
    {
        // Список отсортирован по возрастанию — ищем ближайшую точку к target
        return sorted.MinBy(e => Math.Abs((e.Timestamp - target).TotalSeconds));
    }
}
