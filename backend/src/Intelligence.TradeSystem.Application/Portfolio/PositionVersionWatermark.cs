using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Лёгкий ID/version watermark одной позиции, используемый для pre-lock race detection
/// без materialization полного доменного агрегата и его истории изменений.
/// </summary>
/// <param name="PositionId">Идентификатор позиции.</param>
/// <param name="Version">Версия сохранения, под которой позиция была прочитана.</param>
public readonly record struct PositionVersionWatermark(PositionId PositionId, ConcurrencyVersion Version);
