namespace Intelligence.TradeSystem.Domain.Snapshots;

/// <summary>
/// Значимый уровень ликвидности (liquidity wall) — концентрация большого объёма заявок
/// на одном ценовом уровне, способная оказывать существенное влияние на движение цены.
/// </summary>
public sealed record LiquidityWall
{
    /// <summary>Цена уровня ликвидности.</summary>
    public decimal Price { get; init; }

    /// <summary>Суммарный объём заявок, образующих стену ликвидности.</summary>
    public decimal Size { get; init; }

    /// <summary>
    /// Расстояние от текущей рыночной цены до уровня стены в процентах.
    /// </summary>
    public decimal DistancePctFromMarket { get; init; }
}
