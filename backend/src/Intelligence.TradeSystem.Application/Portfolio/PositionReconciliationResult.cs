using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Результат сопоставления наблюдения открытых позиций с текущими бизнес-позициями.
/// </summary>
public sealed record PositionReconciliationResult(
    IReadOnlyList<Position> NewPositions,
    IReadOnlyList<PositionChange> Changes,
    IReadOnlyList<string> Warnings);

