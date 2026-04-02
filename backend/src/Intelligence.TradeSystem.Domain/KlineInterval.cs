namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Временной интервал одной свечи (таймфрейм).
/// Определяет период агрегации данных о цене и объёме при запросе kline-данных с биржи.
/// </summary>
public enum KlineInterval
{
    /// <summary>1 минута.</summary>
    OneMinute,

    /// <summary>3 минуты.</summary>
    ThreeMinutes,

    /// <summary>5 минут.</summary>
    FiveMinutes,

    /// <summary>15 минут.</summary>
    FifteenMinutes,

    /// <summary>30 минут.</summary>
    ThirtyMinutes,

    /// <summary>1 час.</summary>
    OneHour,

    /// <summary>2 часа.</summary>
    TwoHours,

    /// <summary>4 часа.</summary>
    FourHours,

    /// <summary>6 часов.</summary>
    SixHours,

    /// <summary>12 часов.</summary>
    TwelveHours,

    /// <summary>1 день.</summary>
    OneDay,

    /// <summary>1 неделя.</summary>
    OneWeek,

    /// <summary>1 месяц.</summary>
    OneMonth
}
