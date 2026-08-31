namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Стена ликвидности в стакане заявок.
/// </summary>
public sealed record MarketFactsLiquidityWallPayload
{
    /// <summary>Цена уровня.</summary>
    public decimal? Price { get; init; }

    /// <summary>Объём на уровне.</summary>
    public decimal? Size { get; init; }

    /// <summary>Расстояние от рыночной цены в процентах.</summary>
    public decimal? DistancePctFromMarket { get; init; }
}
