namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Поток сделок за скользящее окно.
/// </summary>
public sealed record MarketFactsTradeFlowPayload
{
    /// <summary>Начало окна наблюдения (UTC).</summary>
    public DateTimeOffset? WindowStartUtc { get; init; }

    /// <summary>Конец окна наблюдения (UTC).</summary>
    public DateTimeOffset? WindowEndUtc { get; init; }

    /// <summary>Объём покупок в окне.</summary>
    public decimal? BuyVolume { get; init; }

    /// <summary>Объём продаж в окне.</summary>
    public decimal? SellVolume { get; init; }

    /// <summary>Дельта объёма (BuyVolume − SellVolume).</summary>
    public decimal? DeltaVolume { get; init; }

    /// <summary>Дельта объёма в процентах от суммарного объёма.</summary>
    public decimal? DeltaPct { get; init; }

    /// <summary>Количество сделок на покупку.</summary>
    public int? BuyTrades { get; init; }

    /// <summary>Количество сделок на продажу.</summary>
    public int? SellTrades { get; init; }

    /// <summary>Средний размер сделки.</summary>
    public decimal? AvgTradeSize { get; init; }

    /// <summary>Максимальный размер сделки в окне.</summary>
    public decimal? MaxTradeSize { get; init; }

    /// <summary>Признак агрессивного давления покупателей.</summary>
    public required bool HasAggressiveBuyPressure { get; init; }

    /// <summary>Признак агрессивного давления продавцов.</summary>
    public required bool HasAggressiveSellPressure { get; init; }

    /// <summary>
    /// Направление потока сделок. Вычисляется mapper'ом.
    /// Ожидаемые значения: <c>buy_dominant</c>, <c>sell_dominant</c>, <c>neutral</c>.
    /// </summary>
    public required string Direction { get; init; }

    /// <summary>
    /// Label потока сделок. Вычисляется mapper'ом.
    /// Ожидаемые значения: <c>aggressive_buying</c>, <c>aggressive_selling</c>,
    /// <c>mixed_aggressive_pressure</c>, <c>neutral</c>.
    /// </summary>
    public required string Label { get; init; }
}
