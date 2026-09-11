using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Явно передаваемые пороги первой версии алгоритма оценки позиции.
/// </summary>
public sealed record PositionAssessmentRules
{
    /// <summary>
    /// Создаёт набор порогов оценки и проверяет его воспроизводимые параметры.
    /// </summary>
    public PositionAssessmentRules(
        RuleVersion version,
        decimal rsiOverboughtThreshold,
        decimal rsiOversoldThreshold,
        decimal nearbyLevelDistancePercent,
        decimal liquidationDangerDistancePercent,
        decimal lowVolumeRatioThreshold,
        TimeSpan validityPeriod)
    {
        if (string.IsNullOrWhiteSpace(version.Value))
            throw new ArgumentException("Assessment rule version must be initialized.", nameof(version));
        if (rsiOversoldThreshold < 0m || rsiOversoldThreshold >= rsiOverboughtThreshold ||
            rsiOverboughtThreshold > 100m)
            throw new ArgumentOutOfRangeException(
                nameof(rsiOverboughtThreshold), "RSI thresholds must satisfy 0 <= oversold < overbought <= 100.");
        ArgumentOutOfRangeException.ThrowIfNegative(nearbyLevelDistancePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(liquidationDangerDistancePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(lowVolumeRatioThreshold);
        if (validityPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validityPeriod), validityPeriod, "Validity period must be positive.");

        Version = version;
        RsiOverboughtThreshold = rsiOverboughtThreshold;
        RsiOversoldThreshold = rsiOversoldThreshold;
        NearbyLevelDistancePercent = nearbyLevelDistancePercent;
        LiquidationDangerDistancePercent = liquidationDangerDistancePercent;
        LowVolumeRatioThreshold = lowVolumeRatioThreshold;
        ValidityPeriod = validityPeriod;
    }

    /// <summary>Версия алгоритма и порогов оценки.</summary>
    public RuleVersion Version { get; }

    /// <summary>Верхний порог экстремально высокого RSI.</summary>
    public decimal RsiOverboughtThreshold { get; }

    /// <summary>Нижний порог экстремально низкого RSI.</summary>
    public decimal RsiOversoldThreshold { get; }

    /// <summary>Максимальное расстояние до уровня, считающееся близким.</summary>
    public decimal NearbyLevelDistancePercent { get; }

    /// <summary>Максимальное безопасное расстояние до ликвидации.</summary>
    public decimal LiquidationDangerDistancePercent { get; }

    /// <summary>Порог отношения объёма, считающегося низким.</summary>
    public decimal LowVolumeRatioThreshold { get; }

    /// <summary>Детерминированный срок актуальности оценки.</summary>
    public TimeSpan ValidityPeriod { get; }

    /// <summary>Пороги первой версии алгоритма оценки.</summary>
    public static PositionAssessmentRules Default => new(
        new RuleVersion("assessment-v1"),
        rsiOverboughtThreshold: 70m,
        rsiOversoldThreshold: 30m,
        nearbyLevelDistancePercent: 1m,
        liquidationDangerDistancePercent: 5m,
        lowVolumeRatioThreshold: 0.5m,
        validityPeriod: TimeSpan.FromMinutes(5));
}
