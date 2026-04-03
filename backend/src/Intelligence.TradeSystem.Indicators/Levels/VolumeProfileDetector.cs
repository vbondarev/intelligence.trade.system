using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Indicators.Levels;

/// <summary>
/// Определяет уровни поддержки и сопротивления методом Volume Profile.
/// <para>
/// Алгоритм: ценовой диапазон [min(Low), max(High)] делится на <see cref="BucketCount"/> бакетов.
/// Объём каждой свечи распределяется пропорционально по всем бакетам, которые она перекрывает.
/// Бакеты с наибольшим накопленным объёмом (High Volume Nodes) становятся ключевыми уровнями:
/// HVN ниже текущей цены → поддержка, выше → сопротивление.
/// </para>
/// </summary>
internal static class VolumeProfileDetector
{
    private const int BucketCount = 100;

    /// <summary>
    /// Возвращает два ближайших уровня поддержки и два уровня сопротивления.
    /// Уровни отсортированы по близости к текущей цене (Support1 — ближайший снизу,
    /// Resistance1 — ближайший сверху).
    /// </summary>
    public static LevelSet Detect(Kline[] klines)
    {
        if (klines.Length == 0)
            return new LevelSet(0m, 0m, 0m, 0m);

        var minPrice = klines.Min(k => k.Low);
        var maxPrice = klines.Max(k => k.High);
        var range    = maxPrice - minPrice;

        if (range == 0m)
            return new LevelSet(minPrice, minPrice, minPrice, minPrice);

        var bucketSize = range / BucketCount;
        var volumes    = new decimal[BucketCount];

        // Распределяем объём каждой свечи пропорционально по бакетам [Low, High]
        foreach (var kline in klines)
        {
            var startIdx = (int)Math.Floor((double)((kline.Low  - minPrice) / bucketSize));
            var endIdx   = (int)Math.Floor((double)((kline.High - minPrice) / bucketSize));

            startIdx = Math.Clamp(startIdx, 0, BucketCount - 1);
            endIdx   = Math.Clamp(endIdx,   0, BucketCount - 1);

            var span         = endIdx - startIdx + 1;
            var volPerBucket = kline.Volume / span;

            for (var i = startIdx; i <= endIdx; i++)
                volumes[i] += volPerBucket;
        }

        var currentPrice = klines[^1].Close;
        var supports     = new List<decimal>(2);
        var resistances  = new List<decimal>(2);

        // Перебираем бакеты по убыванию объёма, отбираем ближайшие к цене HVN
        foreach (var i in Enumerable.Range(0, BucketCount).OrderByDescending(i => volumes[i]))
        {
            if (volumes[i] == 0m) break; // все оставшиеся тоже нулевые — дальше нет смысла

            var bucketMid = minPrice + (i + 0.5m) * bucketSize;

            if (bucketMid < currentPrice && supports.Count < 2)
                supports.Add(bucketMid);
            else if (bucketMid > currentPrice && resistances.Count < 2)
                resistances.Add(bucketMid);

            if (supports.Count == 2 && resistances.Count == 2)
                break;
        }

        // Поддержки: от ближайшей к дальней (по убыванию цены)
        supports.Sort((a, b) => b.CompareTo(a));

        // Сопротивления: от ближайшего к дальнему (по возрастанию цены)
        resistances.Sort();

        return new LevelSet(
            supports.ElementAtOrDefault(0),
            supports.ElementAtOrDefault(1),
            resistances.ElementAtOrDefault(0),
            resistances.ElementAtOrDefault(1));
    }
}
