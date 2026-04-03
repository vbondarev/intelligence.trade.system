namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок истории ставки финансирования за скользящее временное окно.
/// Позволяет оценить сентимент рынка через динамику ставки:
/// устойчиво положительная ставка указывает на перегрев лонгов,
/// отрицательная — на перегрев шортов.
/// </summary>
public sealed record FundingRateSnapshot
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Категория рынка: линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; }

    /// <summary>Начало временного окна (UTC) — момент самой ранней записи.</summary>
    public DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>Конец временного окна (UTC) — момент последнего начисления.</summary>
    public DateTimeOffset WindowEndUtc { get; init; }

    /// <summary>
    /// Текущая ставка финансирования — значение последнего начисления.
    /// </summary>
    public decimal CurrentRate { get; init; }

    /// <summary>
    /// Средняя ставка за последние 24 часа (3 начисления по 8 часов).
    /// Сглаживает краткосрочные всплески.
    /// </summary>
    public decimal Avg24hRate { get; init; }

    /// <summary>
    /// Средняя ставка за последние 7 дней (21 начисление).
    /// Отражает устойчивый сентимент рынка.
    /// </summary>
    public decimal Avg7dRate { get; init; }

    /// <summary>Максимальная ставка в окне.</summary>
    public decimal MaxRate { get; init; }

    /// <summary>Минимальная ставка в окне.</summary>
    public decimal MinRate { get; init; }

    /// <summary>
    /// <c>true</c>, если текущая ставка положительна — лонги платят шортам.
    /// </summary>
    public bool IsPositive { get; init; }

    /// <summary>
    /// Флаг экстремально высокой ставки (бычий перегрев).
    /// Выставляется, когда <c>CurrentRate > ExtremeFundingThreshold</c>.
    /// Контрарный сигнал: перегрев лонгов повышает вероятность коррекции.
    /// </summary>
    public bool IsExtremeBullish { get; init; }

    /// <summary>
    /// Флаг экстремально низкой ставки (медвежий перегрев).
    /// Выставляется, когда <c>CurrentRate &lt; -ExtremeFundingThreshold</c>.
    /// Контрарный сигнал: перегрев шортов повышает вероятность отскока.
    /// </summary>
    public bool IsExtremeBearish { get; init; }
}

