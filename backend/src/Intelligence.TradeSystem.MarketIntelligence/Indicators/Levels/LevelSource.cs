namespace Intelligence.TradeSystem.MarketIntelligence.Indicators.Levels;

/// <summary>
/// Источник, из которого был определён ценовой уровень.
/// </summary>
public enum LevelSource
{
    /// <summary>
    /// Уровень определён через упрощённый анализ профиля объёма (Simplified Volume Profile / HVN-кластер).
    /// <para>
    /// Алгоритм равномерно распределяет объём свечи по диапазону Low–High
    /// и <strong>не является</strong> точным Volume-at-Price.
    /// </para>
    /// </summary>
    SimplifiedVolumeProfile = 0,
}
