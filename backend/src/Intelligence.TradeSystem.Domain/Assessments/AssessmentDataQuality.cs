namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Нормализованное качество входных данных оценки.
/// </summary>
public enum AssessmentDataQuality
{
    /// <summary>Данные свежие, полные и надёжные.</summary>
    FreshCompleteReliable = 0,

    /// <summary>Данные старше допустимого окна.</summary>
    Stale = 1,

    /// <summary>Часть обязательных данных отсутствует или рассчитана по fallback.</summary>
    Partial = 2,

    /// <summary>Данные недостаточно надёжны для безопасной интерпретации.</summary>
    Uncertain = 3,
}

/// <summary>
/// Ограничение повышения риска, вычисленное неотключаемым safety guard.
/// </summary>
public enum AssessmentSafetyState
{
    /// <summary>Safety guard не применялся legacy-контрактом.</summary>
    NotEvaluated = 0,

    /// <summary>Качество данных само по себе не запрещает повышение риска.</summary>
    Allowed = 1,

    /// <summary>Повышение риска запрещено качеством данных.</summary>
    Blocked = 2,
}

/// <summary>Интерпретация рыночного тренда относительно направления позиции.</summary>
public enum PositionTrendAlignment
{
    /// <summary>Тренд поддерживает направление позиции.</summary>
    Aligned = 0,

    /// <summary>Тренд противоречит направлению позиции.</summary>
    Adverse = 1,

    /// <summary>Тренд боковой или не определён.</summary>
    FlatOrUnknown = 2,
}

/// <summary>Нормализованное направление рынка, сохранённое в оценке позиции.</summary>
public enum AssessmentTrendDirection
{
    /// <summary>Направление не определено.</summary>
    Unknown = 0,

    /// <summary>Рынок растёт.</summary>
    Bullish = 1,

    /// <summary>Рынок снижается.</summary>
    Bearish = 2,

    /// <summary>Рынок движется без выраженного направления.</summary>
    Sideways = 3,
}

/// <summary>Состояние моментума по доступному RSI.</summary>
public enum AssessmentMomentumState
{
    /// <summary>RSI находится в нормальном диапазоне.</summary>
    Normal = 0,

    /// <summary>RSI указывает на экстремально высокое значение.</summary>
    Overbought = 1,

    /// <summary>RSI указывает на экстремально низкое значение.</summary>
    Oversold = 2,

    /// <summary>RSI недоступен или ненадёжен.</summary>
    Unavailable = 3,
}

/// <summary>Положение цены относительно другой цены.</summary>
public enum AssessmentPricePosition
{
    /// <summary>Цена ниже ориентира.</summary>
    Below = 0,

    /// <summary>Цена примерно равна ориентиру.</summary>
    At = 1,

    /// <summary>Цена выше ориентира.</summary>
    Above = 2,

    /// <summary>Положение невозможно определить.</summary>
    Unavailable = 3,
}

/// <summary>Состояние защитного стопа относительно текущей цены.</summary>
public enum AssessmentStopState
{
    /// <summary>Стоп расположен на защитной стороне позиции.</summary>
    Protective = 0,

    /// <summary>Стоп известен, но расположен не на защитной стороне.</summary>
    NonProtective = 1,

    /// <summary>Стоп известен, но текущую цену сравнить невозможно.</summary>
    Unknown = 2,

    /// <summary>Стоп отсутствует.</summary>
    Unavailable = 3,
}

/// <summary>Контекст расстояния до цены ликвидации.</summary>
public enum AssessmentLiquidationState
{
    /// <summary>Ликвидация доступна и находится далеко.</summary>
    Far = 0,

    /// <summary>Ликвидация доступна и находится близко.</summary>
    Near = 1,

    /// <summary>Цена ликвидации находится на невозможной стороне рынка.</summary>
    Invalid = 2,

    /// <summary>Цена ликвидации отсутствует или сравнение невозможно.</summary>
    Unavailable = 3,
}
