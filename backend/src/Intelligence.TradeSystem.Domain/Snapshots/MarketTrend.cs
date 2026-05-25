namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Направление рыночного тренда, определённое системой для конкретного таймфрейма.
/// </summary>
public enum MarketTrend
{
    /// <summary>Тренд не определён (недостаточно данных или боковое движение без выраженного направления).</summary>
    Unknown = 0,

    /// <summary>Восходящий тренд: цена устойчиво движется вверх.</summary>
    Bullish = 1,

    /// <summary>Нисходящий тренд: цена устойчиво движется вниз.</summary>
    Bearish = 2,

    /// <summary>Боковое движение (консолидация): отсутствие выраженного направленного тренда.</summary>
    Sideways = 3
}
