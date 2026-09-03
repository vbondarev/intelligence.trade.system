using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Domain.History;

/// <summary>
/// Неизменяемая запись одного существенного изменения бизнес-позиции.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> описывает произошедшее событие (New/Updated/Increased/Reduced/...),
/// а не долговременное состояние позиции — см. <see cref="PositionTrackingState"/>.
/// <see cref="Cause"/> описывает операционную причину, по которой это изменение произошло.
/// </remarks>
public sealed record PositionChange(
    PositionId PositionId,
    PositionChangeKind Kind,
    PositionChangeCause Cause,
    DateTimeOffset OccurredAt,
    PositionTrackingState TrackingStateAfter,
    PositionStateSnapshot? Before,
    PositionStateSnapshot After);
