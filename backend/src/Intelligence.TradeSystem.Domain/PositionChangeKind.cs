namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Тип существенного изменения бизнес-позиции.
/// Описывает произошедшее событие, а не долговременное состояние (см.
/// <see cref="PositionTrackingState"/>).
/// </summary>
public enum PositionChangeKind
{
    /// <summary>Позиция обнаружена впервые (создан новый жизненный цикл).</summary>
    New = 0,

    /// <summary>
    /// Изменились структурно существенные параметры позиции (не размер), например
    /// средняя цена входа, плечо, уровни TP/SL/trailing-stop.
    /// </summary>
    Updated = 1,

    /// <summary>Размер позиции увеличился по сравнению с предыдущим наблюдением.</summary>
    Increased = 2,

    /// <summary>Размер позиции уменьшился (но остался положительным).</summary>
    Reduced = 3,

    /// <summary>
    /// Позиция достоверно отсутствует в полном снимке своей области (scope) и
    /// считается закрытой.
    /// </summary>
    Closed = 4,

    /// <summary>Позиция переведена в состояние <see cref="PositionTrackingState.Unknown"/>.</summary>
    MarkedUnknown = 5,

    /// <summary>Позиция переведена в состояние <see cref="PositionTrackingState.Stale"/>.</summary>
    MarkedStale = 6,

    /// <summary>
    /// Позиция вновь подтверждена достоверным наблюдением после
    /// <see cref="PositionTrackingState.Unknown"/> или <see cref="PositionTrackingState.Stale"/>.
    /// </summary>
    Recovered = 7,
}
