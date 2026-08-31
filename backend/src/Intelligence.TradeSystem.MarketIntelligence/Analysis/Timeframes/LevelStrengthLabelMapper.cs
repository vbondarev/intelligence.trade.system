namespace Intelligence.TradeSystem.MarketIntelligence.Analysis.Timeframes;

/// <summary>
/// Централизованное отображение числового <c>strength ∈ [0, 1]</c> в <see cref="LevelStrengthLabel"/>.
///
/// Инварианты:
/// - strength == null           → <see cref="LevelStrengthLabel.Unavailable"/>
/// - strength &gt;= 0.70        → <see cref="LevelStrengthLabel.Strong"/>
/// - strength &gt;= 0.40        → <see cref="LevelStrengthLabel.Moderate"/>
/// - strength &lt;  0.40        → <see cref="LevelStrengthLabel.Weak"/>
/// </summary>
public static class LevelStrengthLabelMapper
{
    /// <summary>Минимальный strength для метки <see cref="LevelStrengthLabel.Strong"/>.</summary>
    public const decimal StrongThreshold = 0.70m;

    /// <summary>Минимальный strength для метки <see cref="LevelStrengthLabel.Moderate"/>.</summary>
    public const decimal ModerateThreshold = 0.40m;

    /// <summary>
    /// Отображает нормализованный <paramref name="strength"/> в <see cref="LevelStrengthLabel"/>.
    /// </summary>
    /// <param name="strength">Нормализованная сила уровня [0, 1], или <c>null</c> если недоступна.</param>
    /// <returns>Детерминированная метка силы уровня.</returns>
    public static LevelStrengthLabel Map(decimal? strength)
    {
        if (strength is not { } s) return LevelStrengthLabel.Unavailable;
        if (s >= StrongThreshold) return LevelStrengthLabel.Strong;
        if (s >= ModerateThreshold) return LevelStrengthLabel.Moderate;
        return LevelStrengthLabel.Weak;
    }
}
