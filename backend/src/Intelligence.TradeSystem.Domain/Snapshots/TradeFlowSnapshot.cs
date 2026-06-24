namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Снимок потока совершённых сделок за скользящее временное окно.
/// Позволяет оценить агрессивность покупателей и продавцов, а также дельту объёма —
/// ключевой сигнал давления на цену.
/// </summary>
public sealed record TradeFlowSnapshot
{
    /// <summary>Начало временного окна агрегации сделок (UTC).</summary>
    public DateTimeOffset WindowStartUtc { get; init; }

    /// <summary>Конец временного окна агрегации сделок (UTC).</summary>
    public DateTimeOffset WindowEndUtc { get; init; }

    /// <summary>Суммарный объём сделок, инициированных покупателями (taker buy).</summary>
    public decimal BuyVolume { get; init; }

    /// <summary>Суммарный объём сделок, инициированных продавцами (taker sell).</summary>
    public decimal SellVolume { get; init; }

    /// <summary>
    /// Дельта объёма: <c>BuyVolume − SellVolume</c>.
    /// Положительная дельта указывает на преобладание агрессивных покупателей.
    /// </summary>
    public decimal DeltaVolume { get; init; }

    /// <summary>
    /// Дельта объёма в процентах от общего оборота:
    /// <c>DeltaVolume / (BuyVolume + SellVolume) × 100</c>.
    /// Нормализует дельту для сравнения между периодами и инструментами.
    /// </summary>
    public decimal DeltaPct { get; init; }

    /// <summary>Общее количество совершённых сделок в окне.</summary>
    public int TotalTrades { get; init; }

    /// <summary>Количество сделок, инициированных покупателями.</summary>
    public int BuyTrades { get; init; }

    /// <summary>Количество сделок, инициированных продавцами.</summary>
    public int SellTrades { get; init; }

    /// <summary>
    /// Средний размер одной сделки в окне:
    /// <c>(BuyVolume + SellVolume) / TotalTrades</c>.
    /// </summary>
    public decimal AvgTradeSize { get; init; }

    /// <summary>
    /// Максимальный размер одной сделки в окне.
    /// Аномально крупные значения могут указывать на активность институциональных игроков.
    /// </summary>
    public decimal MaxTradeSize { get; init; }

    /// <summary>
    /// Флаг агрессивного давления покупателей.
    /// Устанавливается, когда дельта объёма и доля покупных сделок
    /// превышают пороговые значения.
    /// </summary>
    public bool HasAggressiveBuyPressure { get; init; }

    /// <summary>
    /// Флаг агрессивного давления продавцов.
    /// Устанавливается, когда дельта объёма и доля продажных сделок
    /// превышают пороговые значения.
    /// </summary>
    public bool HasAggressiveSellPressure { get; init; }
}
