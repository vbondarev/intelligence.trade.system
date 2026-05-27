namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Агрегированный снимок стакана заявок на конкретный момент времени.
/// Содержит суммарные объёмы на нескольких глубинах, метрики дисбаланса бид/аск,
/// а также значимые уровни концентрации ликвидности (liquidity walls).
/// </summary>
public sealed record OrderBookSnapshot
{
    /// <summary>Момент времени (UTC), в который был зафиксирован снимок стакана.</summary>
    public DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Лучшая цена покупки (первый уровень бидов).</summary>
    public decimal BestBidPrice { get; init; }

    /// <summary>Лучшая цена продажи (первый уровень асков).</summary>
    public decimal BestAskPrice { get; init; }

    /// <summary>Суммарный объём на топ-5 ценовых уровнях бидов.</summary>
    public decimal TotalBidVolumeTop5 { get; init; }

    /// <summary>Суммарный объём на топ-5 ценовых уровнях асков.</summary>
    public decimal TotalAskVolumeTop5 { get; init; }

    /// <summary>Суммарный объём на топ-10 ценовых уровнях бидов.</summary>
    public decimal TotalBidVolumeTop10 { get; init; }

    /// <summary>Суммарный объём на топ-10 ценовых уровнях асков.</summary>
    public decimal TotalAskVolumeTop10 { get; init; }

    /// <summary>Суммарный объём на топ-20 ценовых уровнях бидов.</summary>
    public decimal TotalBidVolumeTop20 { get; init; }

    /// <summary>Суммарный объём на топ-20 ценовых уровнях асков.</summary>
    public decimal TotalAskVolumeTop20 { get; init; }

    /// <summary>
    /// Дисбаланс стакана по топ-5 уровням: <c>(Bid − Ask) / (Bid + Ask)</c>.
    /// Диапазон [−1, 1]. Положительное значение — давление покупателей, отрицательное — продавцов.
    /// </summary>
    public decimal ImbalanceTop5 { get; init; }

    /// <summary>
    /// Дисбаланс стакана по топ-10 уровням: <c>(Bid − Ask) / (Bid + Ask)</c>.
    /// Диапазон [−1, 1].
    /// </summary>
    public decimal ImbalanceTop10 { get; init; }

    /// <summary>
    /// Дисбаланс стакана по топ-20 уровням: <c>(Bid − Ask) / (Bid + Ask)</c>.
    /// Диапазон [−1, 1].
    /// </summary>
    public decimal ImbalanceTop20 { get; init; }

    /// <summary>Список верхних уровней бидов, отсортированных по убыванию цены.</summary>
    public List<OrderBookLevel> TopBids { get; init; } = [];

    /// <summary>Список верхних уровней асков, отсортированных по возрастанию цены.</summary>
    public List<OrderBookLevel> TopAsks { get; init; } = [];

    /// <summary>
    /// Значимые уровни концентрации объёма на стороне покупки (bid walls).
    /// Могут выступать поддержкой и препятствовать снижению цены.
    /// </summary>
    public List<LiquidityWall> BidWalls { get; init; } = [];

    /// <summary>
    /// Значимые уровни концентрации объёма на стороне продажи (ask walls).
    /// Могут выступать сопротивлением и препятствовать росту цены.
    /// </summary>
    public List<LiquidityWall> AskWalls { get; init; } = [];
}
