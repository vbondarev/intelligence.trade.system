namespace Intelligence.TradeSystem.Application.Realtime.Observations;

/// <summary>Семантика публичного рыночного сообщения.</summary>
public enum MarketPriceUpdateKind
{
    /// <summary>Полное состояние инструмента.</summary>
    Snapshot = 1,

    /// <summary>Только изменившиеся поля; отсутствующее поле не означает отсутствия значения.</summary>
    Delta = 2,
}
