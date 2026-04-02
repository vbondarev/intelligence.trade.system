namespace Intelligence.TradeSystem.Domain;

/// <summary>
/// Текущее состояние цены торгового инструмента — сырые данные с биржи.
/// Содержит лучшие котировки, последнюю цену и статистику за 24 часа.
/// Производные поля (спред, процент изменения в пунктах и т.п.) вычисляются в ассемблере.
/// </summary>
public sealed record Ticker(
    string Symbol,
    MarketCategory Category,
    decimal LastPrice,
    decimal MarkPrice,
    decimal IndexPrice,
    decimal BidPrice,
    decimal BidSize,
    decimal AskPrice,
    decimal AskSize,
    decimal Price24hChangePct,
    decimal High24h,
    decimal Low24h,
    decimal Volume24h,
    decimal Turnover24h)
{
    /// <summary>Тикер торгового инструмента. Например: <c>BTCUSDT</c>.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Категория рынка: спот, линейный или инверсный перпетуал.</summary>
    public MarketCategory Category { get; init; } = Category;

    /// <summary>Цена последней совершённой сделки.</summary>
    public decimal LastPrice { get; init; } = LastPrice;

    /// <summary>
    /// Расчётная (mark) цена, используемая биржей для вычисления нереализованного PnL
    /// и цены ликвидации.
    /// <para>
    /// Актуальна только для <see cref="MarketCategory.Linear"/> и
    /// <see cref="MarketCategory.Inverse"/>; для <see cref="MarketCategory.Spot"/> всегда <c>0</c>.
    /// </para>
    /// </summary>
    public decimal MarkPrice { get; init; } = MarkPrice;

    /// <summary>
    /// Индексная цена — агрегированная по нескольким биржам спотовая цена базового актива.
    /// Используется как ориентир справедливой стоимости.
    /// <para>
    /// Актуальна только для <see cref="MarketCategory.Linear"/> и
    /// <see cref="MarketCategory.Inverse"/>; для <see cref="MarketCategory.Spot"/> всегда <c>0</c>.
    /// </para>
    /// </summary>
    public decimal IndexPrice { get; init; } = IndexPrice;

    /// <summary>Лучшая цена покупки (первый уровень бидов).</summary>
    public decimal BidPrice { get; init; } = BidPrice;

    /// <summary>Объём на лучшем биде (в базовых единицах/контрактах).</summary>
    public decimal BidSize { get; init; } = BidSize;

    /// <summary>Лучшая цена продажи (первый уровень асков).</summary>
    public decimal AskPrice { get; init; } = AskPrice;

    /// <summary>Объём на лучшем аске (в базовых единицах/контрактах).</summary>
    public decimal AskSize { get; init; } = AskSize;

    /// <summary>
    /// Изменение цены за 24 часа в долях единицы (например, <c>0.0234</c> = +2.34 %).
    /// </summary>
    public decimal Price24hChangePct { get; init; } = Price24hChangePct;

    /// <summary>Максимальная цена за последние 24 часа.</summary>
    public decimal High24h { get; init; } = High24h;

    /// <summary>Минимальная цена за последние 24 часа.</summary>
    public decimal Low24h { get; init; } = Low24h;

    /// <summary>Торговый объём за 24 часа (в базовой валюте/контрактах).</summary>
    public decimal Volume24h { get; init; } = Volume24h;

    /// <summary>Оборот за 24 часа (в котируемой валюте, обычно USDT).</summary>
    public decimal Turnover24h { get; init; } = Turnover24h;
}

