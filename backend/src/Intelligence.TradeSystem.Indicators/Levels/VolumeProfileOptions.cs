namespace Intelligence.TradeSystem.Indicators.Levels;

/// <summary>
/// Параметры алгоритма <see cref="VolumeProfileDetector"/>.
/// </summary>
/// <remarks>
/// Используйте <see cref="Default"/> для получения стандартной конфигурации.
/// Для создания нестандартной конфигурации вызовите конструктор с явными значениями.
/// </remarks>
public sealed class VolumeProfileOptions
{
    /// <summary>
    /// Стандартные параметры: <see cref="BucketCount"/> = 100, <see cref="HvnThresholdRatio"/> = 0.70.
    /// </summary>
    public static readonly VolumeProfileOptions Default = new();

    /// <summary>
    /// Количество ценовых бакетов, на которые делится диапазон [min(Low), max(High)].
    /// Должно быть строго положительным.
    /// </summary>
    public int BucketCount { get; }

    /// <summary>
    /// Доля от максимального объёма бакета, выше которой бакет считается высокообъёмным (HVN).
    /// Должна находиться в диапазоне (0, 1].
    /// </summary>
    public decimal HvnThresholdRatio { get; }

    /// <summary>
    /// Создаёт экземпляр с заданными параметрами.
    /// </summary>
    /// <param name="bucketCount">
    /// Количество бакетов. По умолчанию 100.
    /// </param>
    /// <param name="hvnThresholdRatio">
    /// Порог HVN как доля от максимального объёма бакета. По умолчанию 0.70.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Выбрасывается, если <paramref name="bucketCount"/> ≤ 0
    /// или <paramref name="hvnThresholdRatio"/> не входит в диапазон (0, 1].
    /// </exception>
    public VolumeProfileOptions(int bucketCount = 100, decimal hvnThresholdRatio = 0.7m)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount, nameof(bucketCount));

        if (hvnThresholdRatio <= 0m || hvnThresholdRatio > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hvnThresholdRatio),
                hvnThresholdRatio,
                "HvnThresholdRatio must be in the range (0, 1].");
        }

        BucketCount = bucketCount;
        HvnThresholdRatio = hvnThresholdRatio;
    }
}
