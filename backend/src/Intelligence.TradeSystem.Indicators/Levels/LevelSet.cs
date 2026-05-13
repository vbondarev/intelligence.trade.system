namespace Intelligence.TradeSystem.Indicators.Levels;

/// <summary>
/// Четыре ключевых ценовых уровня, определённых через Volume Profile:
/// два уровня поддержки (ближайшие снизу) и два уровня сопротивления (ближайшие сверху).
/// <c>null</c> означает, что уровень не был обнаружен.
/// </summary>
public sealed record LevelSet(
    decimal? Support1,
    decimal? Support2,
    decimal? Resistance1,
    decimal? Resistance2
);
