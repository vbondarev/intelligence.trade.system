namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>Состояние потокового транспорта биржи.</summary>
public enum RealtimeTransportStatus
{
    /// <summary>Подписки подтверждены биржей.</summary>
    Subscribed = 1,

    /// <summary>Подписка отклонена, включая отказ аутентификации приватного потока.</summary>
    SubscribeFailed = 2,

    /// <summary>Соединение потеряно; события за время разрыва могут быть пропущены.</summary>
    ConnectionLost = 3,

    /// <summary>
    /// Транспорт восстановлен библиотекой биржи. Пропущенные за время разрыва события не
    /// воспроизводятся, поэтому состояние требует REST recovery.
    /// </summary>
    Restored = 4,

    /// <summary>После переподключения подписку восстановить не удалось.</summary>
    ResubscribeFailed = 5,

    /// <summary>
    /// Полученное сообщение не удалось нормализовать либо получатель не принял наблюдение или
    /// сигнал транспорта; событие могло быть потеряно.
    /// </summary>
    MessageRejected = 6,
}
