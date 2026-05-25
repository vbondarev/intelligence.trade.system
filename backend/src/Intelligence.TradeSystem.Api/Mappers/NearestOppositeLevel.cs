namespace Intelligence.TradeSystem.Api.Mappers;

/// <summary>
/// Ближайший противоположный уровень, используемый как препятствие при оценке EntryQuality.
/// <list type="bullet">
///   <item>Для Bullish-входа — ближайший resistance выше цены.</item>
///   <item>Для Bearish-входа — ближайший support ниже цены.</item>
/// </list>
/// Может быть взят как с текущего таймфрейма, так и со старшего.
/// </summary>
/// <param name="DistancePct">Расстояние от текущей цены до уровня в процентах (≥ 0).</param>
/// <param name="Strength">
/// Нормализованная сила уровня [0, 1].
/// <c>null</c> — сила неизвестна; интерпретируется консервативно.
/// </param>
internal sealed record NearestOppositeLevel(decimal DistancePct, decimal? Strength);
