namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Статус открытой позиции на Bybit.
/// Доменный аналог <c>Bybit.Net.Enums.PositionStatus</c>;
/// исключает зависимость слоя Domain от внешних библиотек.
/// </summary>
public enum PositionStatus
{
    /// <summary>Позиция активна, торгуется в штатном режиме.</summary>
    Normal = 0,

    /// <summary>Позиция находится в процессе принудительной ликвидации.</summary>
    Liquidation = 1,

    /// <summary>Позиция закрывается механизмом автоделевериджа (ADL).</summary>
    AutoDeleverage = 2,

    /// <summary>Позиция неактивна (нулевой размер или архив).</summary>
    Inactive = 3,
}
