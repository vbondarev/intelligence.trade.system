namespace Intelligence.TradeSystem.Application.Realtime.State;

/// <summary>
/// Сохранённое состояние приватного realtime-наблюдения аккаунта.
/// </summary>
/// <remarks>
/// Отсутствие сохранённого состояния означает, что состояние наблюдения неизвестно.
/// <see cref="Connected"/> без свежего heartbeat не подтверждает работающий транспорт:
/// см. <see cref="RealtimeObservationState.IsTransportOperational"/>.
/// </remarks>
public enum RealtimeObservationTransportStatus
{
    /// <summary>Наблюдатель текущего поколения запускается; подписки ещё не подтверждены.</summary>
    Initializing = 1,

    /// <summary>Подписки подтверждены.</summary>
    Connected = 2,

    /// <summary>Соединение потеряно, выполняется восстановление.</summary>
    Reconnecting = 3,

    /// <summary>Наблюдение не гарантирует полноту и требует вмешательства или recovery.</summary>
    Degraded = 4,

    /// <summary>Наблюдатель остановлен, например из-за отключения аккаунта или смены credentials.</summary>
    Stopped = 5,
}
