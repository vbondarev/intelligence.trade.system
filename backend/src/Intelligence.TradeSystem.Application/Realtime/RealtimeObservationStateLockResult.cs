namespace Intelligence.TradeSystem.Application.Realtime;

/// <summary>
/// Результат проверки поколения и persisted recovery-состояния под транзакционной блокировкой.
/// </summary>
public enum RealtimeObservationStateLockResult
{
    /// <summary>Поколение не текущее либо состояние недоступно в области пользователя.</summary>
    GenerationSuperseded,

    /// <summary>Поколение текущее, recovery не требуется, блокировка удерживается.</summary>
    Locked,

    /// <summary>Поколение текущее, но persisted recovery ещё не подтверждён.</summary>
    RecoveryRequired,
}
