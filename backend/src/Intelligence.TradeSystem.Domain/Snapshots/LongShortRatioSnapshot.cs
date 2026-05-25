namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок динамики соотношения лонг/шорт позиций за скользящее временное окно.
/// Позволяет оценить позиционирование толпы и выявить экстремальные значения,
/// которые являются контрарными сигналами разворота.
/// </summary>
public sealed record LongShortRatioSnapshot
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; }

    /// <summary>Период агрегации, с которым были запрошены данные.</summary>
    public LongShortRatioPeriod Period { get; init; }

    /// <summary>Начало временного окна агрегации (UTC).</summary>
    public DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>Конец временного окна агрегации (UTC) — момент последней точки.</summary>
    public DateTimeOffset WindowEndUtc { get; init; }

    /// <summary>
    /// Текущая доля лонгов — значение последней точки ряда.
    /// Диапазон [0, 1]; значение выше 0.5 означает преобладание лонгов.
    /// </summary>
    public decimal CurrentBuyRatio { get; init; }

    /// <summary>
    /// Текущая доля шортов — значение последней точки ряда.
    /// Диапазон [0, 1]; значение выше 0.5 означает преобладание шортов.
    /// </summary>
    public decimal CurrentSellRatio { get; init; }

    /// <summary>
    /// Средняя доля лонгов по всему временному окну.
    /// Позволяет оценить устойчивость позиционирования участников.
    /// </summary>
    public decimal AvgBuyRatio { get; init; }

    /// <summary>
    /// Средняя доля шортов по всему временному окну.
    /// </summary>
    public decimal AvgSellRatio { get; init; }

    /// <summary>
    /// <c>true</c>, если текущая доля лонгов превышает 0.5 — лонги доминируют.
    /// </summary>
    public bool IsLongDominant { get; init; }

    /// <summary>
    /// Флаг экстремального преобладания лонгов.
    /// Устанавливается, когда <c>CurrentBuyRatio > ExtremeLongThreshold</c>.
    /// Контрарный сигнал: перегрев лонгов повышает вероятность нисходящей коррекции.
    /// </summary>
    public bool IsExtremelyLong { get; init; }

    /// <summary>
    /// Флаг экстремального преобладания шортов.
    /// Устанавливается, когда <c>CurrentBuyRatio &lt; (1 − ExtremeLongThreshold)</c>.
    /// Контрарный сигнал: перегрев шортов повышает вероятность восходящего отскока.
    /// </summary>
    public bool IsExtremelyShort { get; init; }
}
