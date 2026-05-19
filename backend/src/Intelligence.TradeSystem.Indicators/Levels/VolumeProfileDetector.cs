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
public static class VolumeProfileDetector
{
    /// <summary>
    /// Возвращает два ближайших уровня поддержки и два ближайших уровня сопротивления.
    /// </summary>
    /// <param name="klines">Массив свечей. Не может быть <see langword="null"/>.</param>
    /// <param name="options">
    /// Параметры алгоритма. Если <see langword="null"/>, используются <see cref="VolumeProfileOptions.Default"/>.
    /// </param>
    public static LevelSet Detect(Kline[] klines, VolumeProfileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(klines);

        options ??= VolumeProfileOptions.Default;

        var bucketCount = options.BucketCount;
        var hvnThresholdRatio = options.HvnThresholdRatio;

        if (klines.Length == 0)
        {
            return new LevelSet(null, null, null, null);
        }

        var minPrice = klines.Min(k => k.Low);
        var maxPrice = klines.Max(k => k.High);
        var range = maxPrice - minPrice;

        if (range == 0m)
        {
            return new LevelSet(null, null, null, null);
        }

        var bucketSize = range / bucketCount;
        var volumes = new decimal[bucketCount];

        foreach (var kline in klines)
        {
            var startIdx = GetBucketIndex(kline.Low, minPrice, bucketSize);
            var endIdx = GetBucketIndex(kline.High, minPrice, bucketSize);

            startIdx = Math.Clamp(startIdx, 0, bucketCount - 1);
            endIdx = Math.Clamp(endIdx, 0, bucketCount - 1);

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
            return new LevelSet(null, null, null, null);
        }

        var threshold = maxVolume * hvnThresholdRatio;
        var currentPrice = klines[^1].Close;

        var hvnClusters = BuildClusters(volumes, minPrice, bucketSize, threshold);

        var supports = hvnClusters
            .Where(x => x.Price < currentPrice)
            .OrderByDescending(x => x.Price)
            .Take(2)
            .ToList();

        var resistances = hvnClusters
            .Where(x => x.Price > currentPrice)
            .OrderBy(x => x.Price)
            .Take(2)
            .ToList();

        return new LevelSet(
            supports.Count > 0 ? supports[0] : null,
            supports.Count > 1 ? supports[1] : null,
            resistances.Count > 0 ? resistances[0] : null,
            resistances.Count > 1 ? resistances[1] : null);
    }

    private static int GetBucketIndex(decimal price, decimal minPrice, decimal bucketSize)
    {
        if (bucketSize <= 0m)
        {
            return 0;
        }

        return (int)((price - minPrice) / bucketSize);
    }

    private static List<LevelInfo> BuildClusters(
        decimal[] volumes,
        decimal minPrice,
        decimal bucketSize,
        decimal threshold)
    {
        // First pass: collect raw cluster data (volume-weighted price centre + total cluster volume).
        var raw = new List<(decimal Price, decimal ClusterVolume, bool IsFallback)>();
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
                raw.Add((weightedPriceSum / weightedVolumeSum, weightedVolumeSum, false));
            }
            else
            {
                var fallbackMid = minPrice + ((start + end + 1) / 2m) * bucketSize;
                raw.Add((fallbackMid, 0m, true));
            }
        }

        if (raw.Count == 0)
            return [];

        // Second pass: normalize strength by the largest cluster volume so that the
        // dominant cluster always receives Strength = 1.0 and all others are relative to it.
        // This guarantees Strength ∈ [0, 1] regardless of how many buckets a cluster spans.
        var maxClusterVolume = raw.Max(c => c.ClusterVolume);

        var clusters = new List<LevelInfo>(raw.Count);
        foreach (var (price, clusterVolume, isFallback) in raw)
        {
            if (isFallback)
            {
                clusters.Add(new LevelInfo(price, 0m, LevelSource.VolumeProfile, 0m));
            }
            else
            {
                var strength = maxClusterVolume > 0m
                    ? Math.Round(clusterVolume / maxClusterVolume, 4)
                    : 0m;
                clusters.Add(new LevelInfo(price, strength, LevelSource.VolumeProfile, clusterVolume));
            }
        }

        return clusters;
    }
}
