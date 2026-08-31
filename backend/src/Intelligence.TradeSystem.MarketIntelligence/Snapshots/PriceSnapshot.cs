namespace Intelligence.TradeSystem.MarketIntelligence.Snapshots;

/// <summary>
/// Снимок текущего состояния цены инструмента: лучшие котировки, спред,
/// а также статистика изменения цены и объёма за последние 24 часа.
/// </summary>
public sealed record PriceSnapshot
{
    /// <summary>Цена последней совершённой сделки.</summary>
    public decimal LastPrice { get; init; }

    /// <summary>
    /// Расчётная (mark) цена, используемая биржей для вычисления нереализованного PnL
    /// и цены ликвидации. Снижает влияние манипуляций на спотовую цену.
    /// </summary>
    public decimal MarkPrice { get; init; }

    /// <summary>
    /// Индексная цена — агрегированная по нескольким биржам спотовая цена базового актива.
    /// Используется как ориентир справедливой стоимости.
    /// </summary>
    public decimal IndexPrice { get; init; }

    /// <summary>Лучшая цена покупки (лучший бид) в стакане заявок.</summary>
    public decimal BidPrice { get; init; }

    /// <summary>Лучшая цена продажи (лучший аск) в стакане заявок.</summary>
    public decimal AskPrice { get; init; }

    /// <summary>Объём (количество контрактов/монет) на лучшем биде.</summary>
    public decimal BidSize { get; init; }

    /// <summary>Объём (количество контрактов/монет) на лучшем аске.</summary>
    public decimal AskSize { get; init; }

    /// <summary>Абсолютный спред: <c>AskPrice − BidPrice</c>.</summary>
    public decimal SpreadAbs { get; init; }

    /// <summary>
    /// Относительный спред в процентах: <c>SpreadAbs / MidPrice × 100</c>.
    /// Характеризует ликвидность инструмента.
    /// </summary>
    public decimal SpreadPct { get; init; }

    /// <summary>Изменение цены за последние 24 часа в процентах.</summary>
    public decimal Price24hChangePct { get; init; }

    /// <summary>Максимальная цена за последние 24 часа.</summary>
    public decimal High24h { get; init; }

    /// <summary>Минимальная цена за последние 24 часа.</summary>
    public decimal Low24h { get; init; }

    /// <summary>Торговый объём за последние 24 часа (в базовой валюте/контрактах).</summary>
    public decimal Volume24h { get; init; }

    /// <summary>Оборот за последние 24 часа (в котируемой валюте, обычно USDT).</summary>
    public decimal Turnover24h { get; init; }
}
