using Intelligence.TradeSystem.Domain.History;

namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Операционная причина существенного изменения позиции (<see cref="PositionChange"/>).
/// Не путать с будущим <c>ReasonCode</c> (этап B-09), который относится к причинам
/// рекомендаций/решений по риску, а не к причинам изменения состояния позиции.
/// </summary>
public enum PositionChangeCause
{
    /// <summary>Позиция обнаружена первым достоверным наблюдением.</summary>
    InitialObservation = 0,

    /// <summary>Изменение вызвано очередным достоверным наблюдением с биржи.</summary>
    ExchangeObservation = 1,

    /// <summary>
    /// Позиция отсутствует в полном (<see cref="OpenPositionsObservationStatus.Complete"/>)
    /// снимке своей области (scope) и считается закрытой.
    /// </summary>
    MissingFromCompleteObservation = 2,

    /// <summary>Получить состояние открытых позиций не удалось (<c>Failed</c>).</summary>
    PositionsObservationFailed = 3,

    /// <summary>
    /// Наблюдение неполное (<c>Partial</c>): позиция не найдена в наборе, но это не
    /// доказывает её закрытие.
    /// </summary>
    PartialObservation = 4,

    /// <summary>
    /// Последнее подтверждённое наблюдение устарело относительно допустимого возраста данных.
    /// </summary>
    FreshnessExpired = 5,

    /// <summary>
    /// Позиция вновь подтверждена достоверным наблюдением после <c>Unknown</c> или <c>Stale</c>.
    /// </summary>
    ObservationRestored = 6,
}
