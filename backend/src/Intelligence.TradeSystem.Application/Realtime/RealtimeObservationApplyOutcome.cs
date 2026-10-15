namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Итог применения одного приватного потокового наблюдения к бизнес-состоянию аккаунта.
/// </summary>
public enum RealtimeObservationApplyOutcome
{
    /// <summary>Наблюдение принято; изменения, курсор и события outbox зафиксированы одной транзакцией.</summary>
    Applied = 1,

    /// <summary>Повтор уже принятой ревизии; состояние и события не изменены.</summary>
    Duplicate = 2,

    /// <summary>Новизна наблюдения не доказана относительно сохранённого состояния; наблюдение отброшено.</summary>
    Stale = 3,

    /// <summary>Наблюдение вне поддерживаемой области и не влияет на состояние.</summary>
    Ignored = 4,

    /// <summary>Порядок или полнота не подтверждены; требуется полная REST-сверка аккаунта.</summary>
    RecoveryRequired = 5,

    /// <summary>Поколение наблюдателя больше не текущее; наблюдение отброшено.</summary>
    GenerationSuperseded = 6,

    /// <summary>Аккаунт отсутствует в области пользователя, отключён или его credentials недоступны.</summary>
    AccountUnavailable = 7,
}
