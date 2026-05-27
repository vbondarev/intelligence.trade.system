namespace Intelligence.TradeSystem.Indicators.Levels;

/// <summary>
/// Описывает обнаруженный ценовой уровень с метаданными о его силе и происхождении.
/// </summary>
/// <param name="Price">
/// Цена уровня — взвешенный по объёму центр HVN-кластера.
/// </param>
/// <param name="Strength">
/// Относительная сила уровня в диапазоне [0, 1].
/// Вычисляется как <c>ClusterVolume / maxClusterVolume</c> профиля.
/// Значение 1.0 означает, что кластер содержит максимальный объём в профиле.
/// </param>
/// <param name="Source">
/// Метод, которым был определён уровень.
/// </param>
/// <param name="ClusterVolume">
/// Суммарный объём бакетов, образующих кластер.
/// </param>
public sealed record LevelInfo(
    decimal Price,
    decimal Strength,
    LevelSource Source,
    decimal ClusterVolume
);
