using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Indicators.Levels;

/// <summary>
/// Определяет уровни поддержки и сопротивления методом упрощённого Volume Profile.
/// </summary>
/// <remarks>
/// Алгоритм:
/// <list type="number">
/// <item><description>Ценовой диапазон [min(Low), max(High)] делится на фиксированное число бакетов.</description></item>
/// <item><description>Объём каждой свечи равномерно распределяется по бакетам, которые она перекрывает.</description></item>
/// <item><description>Накапливается объём по каждому бакету.</description></item>
/// <item><description>Сильные соседние бакеты объединяются в кластеры HVN.</description></item>
/// <item><description>Из кластеров выбираются два ближайших уровня поддержки и два ближайших уровня сопротивления относительно текущей цены.</description></item>
/// </list>
/// </remarks>
internal static class VolumeProfileDetector
{
    private const int BucketCount = 100;
    private const decimal HvnThresholdRatio = 0.7m; // кластер считаем сильным, если бакет >= 70% от max volume

    /// <summary>
    /// Возвращает два ближайших уровня поддержки и два ближайших уровня сопротивления.
    /// </summary>
    public static LevelSet Detect(Kline[] klines)
    {
        ArgumentNullException.ThrowIfNull(klines);

        if (klines.Length == 0)
        {
            return new LevelSet(0m, 0m, 0m, 0m);
        }

        var minPrice = klines.Min(k => k.Low);
        var maxPrice = klines.Max(k => k.High);
        var range = maxPrice - minPrice;

        if (range == 0m)
        {
            return new LevelSet(minPrice, minPrice, minPrice, minPrice);
        }

        var bucketSize = range / BucketCount;
        var volumes = new decimal[BucketCount];

        foreach (var kline in klines)
        {
            var startIdx = GetBucketIndex(kline.Low, minPrice, bucketSize);
            var endIdx = GetBucketIndex(kline.High, minPrice, bucketSize);

            startIdx = Math.Clamp(startIdx, 0, BucketCount - 1);
            endIdx = Math.Clamp(endIdx, 0, BucketCount - 1);

            if (endIdx < startIdx)
            {
                (startIdx, endIdx) = (endIdx, startIdx);
            }

            var span = endIdx - startIdx + 1;
            if (span <= 0)
            {
                continue;
            }

            var volPerBucket = kline.Volume / span;

            for (var i = startIdx; i <= endIdx; i++)
            {
                volumes[i] += volPerBucket;
            }
        }

        var maxVolume = volumes.Max();
        if (maxVolume <= 0m)
        {
            return new LevelSet(0m, 0m, 0m, 0m);
        }

        var threshold = maxVolume * HvnThresholdRatio;
        var currentPrice = klines[^1].Close;

        var hvnClusters = BuildClusters(volumes, minPrice, bucketSize, threshold);

        var supports = hvnClusters
            .Where(x => x < currentPrice)
            .OrderByDescending(x => x)
            .Take(2)
            .ToList();

        var resistances = hvnClusters
            .Where(x => x > currentPrice)
            .OrderBy(x => x)
            .Take(2)
            .ToList();

        return new LevelSet(
            supports.ElementAtOrDefault(0),
            supports.ElementAtOrDefault(1),
            resistances.ElementAtOrDefault(0),
            resistances.ElementAtOrDefault(1));
    }

    private static int GetBucketIndex(decimal price, decimal minPrice, decimal bucketSize)
    {
        if (bucketSize <= 0m)
        {
            return 0;
        }

        return (int)((price - minPrice) / bucketSize);
    }

    private static List<decimal> BuildClusters(decimal[] volumes, decimal minPrice, decimal bucketSize, decimal threshold)
    {
        var clusters = new List<decimal>();
        var i = 0;

        while (i < volumes.Length)
        {
            if (volumes[i] < threshold)
            {
                i++;
                continue;
            }

            var start = i;
            var weightedVolumeSum = 0m;
            var weightedPriceSum = 0m;

            while (i < volumes.Length && volumes[i] >= threshold)
            {
                var bucketMid = minPrice + (i + 0.5m) * bucketSize;
                weightedVolumeSum += volumes[i];
                weightedPriceSum += bucketMid * volumes[i];
                i++;
            }

            var end = i - 1;

            if (weightedVolumeSum > 0m)
            {
                var clusterCenter = weightedPriceSum / weightedVolumeSum;
                clusters.Add(clusterCenter);
            }
            else
            {
                var fallbackMid = minPrice + ((start + end + 1) / 2m) * bucketSize;
                clusters.Add(fallbackMid);
            }
        }

        return clusters;
    }
}
