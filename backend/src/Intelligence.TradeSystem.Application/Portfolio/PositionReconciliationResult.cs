using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Application.Portfolio;

/// <summary>
/// Результат сопоставления наблюдения открытых позиций с текущими бизнес-позициями.
/// </summary>
/// <remarks>
/// <see cref="Changes"/> содержит только новые записи доменной истории, созданные этим вызовом сопоставления.
/// Загруженная история никогда не включается, поэтому эти записи являются единственным источником для жизненного цикла
/// прикладных событий в текущей попытке сохранения.
/// </remarks>
public sealed record PositionReconciliationResult(
    IReadOnlyList<Position> NewPositions,
    IReadOnlyList<PositionChange> Changes,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Существующие позиции, чьё текущее состояние затронуто этим наблюдением.
    /// Включает обновления только динамических данных, не создающие запись истории.
    /// </summary>
    public IReadOnlyList<Position> PositionsToPersist { get; init; } = [];

    /// <summary>
    /// Указывает, что наблюдение полностью охватило запрошенную область без
    /// неоднозначности сопоставления, поэтому отсутствие можно считать признаком закрытия.
    /// </summary>
    public bool IsFullyReconciled { get; init; }
}
