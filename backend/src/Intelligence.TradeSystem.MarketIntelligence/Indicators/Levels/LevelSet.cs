namespace Intelligence.TradeSystem.MarketIntelligence.Indicators.Levels;

/// <summary>
/// Четыре ключевых ценовых уровня, определённых через Volume Profile:
/// два уровня поддержки (ближайшие снизу) и два уровня сопротивления (ближайшие сверху).
/// <c>null</c> означает, что уровень не был обнаружен.
/// </summary>
public sealed record LevelSet(
    LevelInfo? Support1,
    LevelInfo? Support2,
    LevelInfo? Resistance1,
    LevelInfo? Resistance2
);
