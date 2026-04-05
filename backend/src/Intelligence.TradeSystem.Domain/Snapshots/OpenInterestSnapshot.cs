namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок динамики открытого интереса за скользящее временное окно.
/// Позволяет оценить аккумуляцию или дистрибуцию позиций участников рынка
/// и изменение интереса относительно прошлых периодов.
/// </summary>
public sealed record OpenInterestSnapshot
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; }

    /// <summary>Интервал агрегации, с которым были запрошены данные.</summary>
    public OpenInterestInterval Interval { get; init; }

    /// <summary>Начало временного окна агрегации (UTC).</summary>
    public DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>Конец временного окна агрегации (UTC) — момент последней точки.</summary>
    public DateTimeOffset WindowEndUtc { get; init; }

    /// <summary>
    /// Текущий открытый интерес — значение последней (самой свежей) точки ряда.
    /// </summary>
    public decimal CurrentOpenInterest { get; init; }

    /// <summary>
    /// Максимальный открытый интерес в окне.
    /// Аномально высокое значение может указывать на перегрев позиционирования.
    /// </summary>
    public decimal PeakOpenInterest { get; init; }

    /// <summary>
    /// Минимальный открытый интерес в окне.
    /// Аномально низкое значение может указывать на массовое закрытие позиций.
    /// </summary>
    public decimal TroughOpenInterest { get; init; }

    /// <summary>
    /// Изменение открытого интереса за последний 1 час в процентах:
    /// <c>(current − oi1hAgo) / oi1hAgo × 100</c>.
    /// В качестве <c>oi1hAgo</c> используется точка, ближайшая к горизонту <c>−1ч</c>.
    /// Возвращает <c>0</c>, если у ближайшей точки открытый интерес равен <c>0</c>.
    /// </summary>
    public decimal Change1hPct { get; init; }

    /// <summary>
    /// Изменение открытого интереса за последние 4 часа в процентах:
    /// <c>(current − oi4hAgo) / oi4hAgo × 100</c>.
    /// В качестве <c>oi4hAgo</c> используется точка, ближайшая к горизонту <c>−4ч</c>.
    /// Возвращает <c>0</c>, если у ближайшей точки открытый интерес равен <c>0</c>.
    /// </summary>
    public decimal Change4hPct { get; init; }

    /// <summary>
    /// Флаг аккумуляции: изменение OI за 1ч превышает <c>+TrendThresholdPct</c>.
    /// Указывает на открытие новых позиций — рост интереса к инструменту.
    /// </summary>
    public bool IsAccumulating { get; init; }

    /// <summary>
    /// Флаг дистрибуции: изменение OI за 1ч ниже <c>−TrendThresholdPct</c>.
    /// Указывает на закрытие позиций — снижение интереса к инструменту.
    /// </summary>
    public bool IsDistributing { get; init; }
}

