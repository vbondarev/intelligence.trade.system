namespace Intelligence.TradeSystem.Api.Models.MarketFacts;

/// <summary>
/// Состояние стакана заявок.
/// </summary>
public sealed record MarketFactsOrderBookPayload
{
    /// <summary>Момент времени (UTC) снятия снапшота стакана.</summary>
    public DateTimeOffset? CapturedAtUtc { get; init; }

    /// <summary>Лучшая цена bid.</summary>
    public decimal? BestBidPrice { get; init; }

    /// <summary>Лучшая цена ask.</summary>
    public decimal? BestAskPrice { get; init; }

    /// <summary>Абсолютный спред между best ask и best bid.</summary>
    public decimal? SpreadAbs { get; init; }

    /// <summary>Спред в процентах от best bid.</summary>
    public decimal? SpreadPct { get; init; }

    /// <summary>Суммарный объём bid в топ-5 уровнях.</summary>
    public decimal? TotalBidVolumeTop5 { get; init; }

    /// <summary>Суммарный объём ask в топ-5 уровнях.</summary>
    public decimal? TotalAskVolumeTop5 { get; init; }

    /// <summary>Суммарный объём bid в топ-10 уровнях.</summary>
    public decimal? TotalBidVolumeTop10 { get; init; }

    /// <summary>Суммарный объём ask в топ-10 уровнях.</summary>
    public decimal? TotalAskVolumeTop10 { get; init; }

    /// <summary>Суммарный объём bid в топ-20 уровнях.</summary>
    public decimal? TotalBidVolumeTop20 { get; init; }

    /// <summary>Суммарный объём ask в топ-20 уровнях.</summary>
    public decimal? TotalAskVolumeTop20 { get; init; }

    /// <summary>Дисбаланс bid/ask в топ-5 уровнях. Положительный — bid доминирует.</summary>
    public decimal? ImbalanceTop5 { get; init; }

    /// <summary>Дисбаланс bid/ask в топ-10 уровнях.</summary>
    public decimal? ImbalanceTop10 { get; init; }

    /// <summary>Дисбаланс bid/ask в топ-20 уровнях.</summary>
    public decimal? ImbalanceTop20 { get; init; }

    /// <summary>Крупные стены ликвидности на стороне bid.</summary>
    public required IReadOnlyList<MarketFactsLiquidityWallPayload> BidWalls { get; init; }

    /// <summary>Крупные стены ликвидности на стороне ask.</summary>
    public required IReadOnlyList<MarketFactsLiquidityWallPayload> AskWalls { get; init; }

    /// <summary>Label давления стакана. Например: <c>bid_heavy</c>, <c>ask_heavy</c>, <c>balanced</c>.</summary>
    public string? PressureLabel { get; init; }

    /// <summary>Label перекоса ликвидности. Например: <c>bid_skewed</c>, <c>ask_skewed</c>.</summary>
    public string? LiquiditySkewLabel { get; init; }
}
