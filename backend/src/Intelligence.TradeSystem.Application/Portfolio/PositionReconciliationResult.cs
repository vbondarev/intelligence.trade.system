using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Результат сопоставления наблюдения открытых позиций с текущими бизнес-позициями.
/// </summary>
public sealed record PositionReconciliationResult(
    IReadOnlyList<Position> NewPositions,
    IReadOnlyList<PositionChange> Changes,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Existing positions whose current state was affected by this observation.
    /// Includes dynamic-only updates that do not create a history record.
    /// </summary>
    public IReadOnlyList<Position> PositionsToPersist { get; init; } = [];

    /// <summary>
    /// Indicates that the observation covered its requested scope completely and without
    /// mapping ambiguity, so absence can be used as evidence of closure.
    /// </summary>
    public bool IsFullyReconciled { get; init; }
}
