namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Временной период агрегации соотношения лонг/шорт позиций.
/// Доменный аналог <c>Bybit.Net.Enums.DataPeriod</c>; исключает зависимость
/// слоя Domain от внешних библиотек.
/// </summary>
public enum LongShortRatioPeriod
{
    /// <summary>5 минут.</summary>
    FiveMinutes,

    /// <summary>15 минут.</summary>
    FifteenMinutes,

    /// <summary>30 минут.</summary>
    ThirtyMinutes,

    /// <summary>1 час.</summary>
    OneHour,

    /// <summary>4 часа.</summary>
    FourHours,

    /// <summary>1 день.</summary>
    OneDay
}

