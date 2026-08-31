namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Текущее состояние цены инструмента.
/// </summary>
public sealed record MarketFactsPricePayload
{
    /// <summary>Последняя цена сделки.</summary>
    public decimal? LastPrice { get; init; }

    /// <summary>Mark price.</summary>
    public decimal? MarkPrice { get; init; }

    /// <summary>Index price.</summary>
    public decimal? IndexPrice { get; init; }

    /// <summary>Абсолютный спред между mark и index price.</summary>
    public decimal? SpreadAbs { get; init; }

    /// <summary>Спред между mark и index price в процентах.</summary>
    public decimal? SpreadPct { get; init; }

    /// <summary>Изменение цены за 24 часа в процентах.</summary>
    public decimal? Price24hChangePct { get; init; }

    /// <summary>Максимальная цена за 24 часа.</summary>
    public decimal? High24h { get; init; }

    /// <summary>Минимальная цена за 24 часа.</summary>
    public decimal? Low24h { get; init; }

    /// <summary>Объём торгов за 24 часа.</summary>
    public decimal? Volume24h { get; init; }
}
