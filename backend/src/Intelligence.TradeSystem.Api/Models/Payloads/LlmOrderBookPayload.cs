namespace Intelligence.TradeSystem.Api.Models.Payloads;

/// <summary>Состояние стакана заявок (без <c>topBids</c> и <c>topAsks</c>).</summary>
public sealed record LlmOrderBookPayload
{
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required decimal BestBidPrice { get; init; }
    public required decimal BestAskPrice { get; init; }

    /// <summary>Абсолютный спред, вычисленный из <c>bestAskPrice - bestBidPrice</c>.</summary>
    public required decimal SpreadAbs { get; init; }

    /// <summary>Относительный спред в процентах, вычисленный из mid-price.</summary>
    public required decimal SpreadPct { get; init; }

    public required decimal TotalBidVolumeTop5 { get; init; }
    public required decimal TotalAskVolumeTop5 { get; init; }
    public required decimal TotalBidVolumeTop10 { get; init; }
    public required decimal TotalAskVolumeTop10 { get; init; }
    public required decimal TotalBidVolumeTop20 { get; init; }
    public required decimal TotalAskVolumeTop20 { get; init; }
    public required decimal ImbalanceTop5 { get; init; }
    public required decimal ImbalanceTop10 { get; init; }
    public required decimal ImbalanceTop20 { get; init; }
    public required IReadOnlyList<LlmLiquidityWallPayload> BidWalls { get; init; }
    public required IReadOnlyList<LlmLiquidityWallPayload> AskWalls { get; init; }

    /// <summary>Метка давления стакана, вычисленная из <c>imbalanceTop10</c>.</summary>
    public required string PressureLabel { get; init; }

    /// <summary>Метка перекоса ликвидности, вычисленная из соотношения bid/ask Top20.</summary>
    public required string LiquiditySkewLabel { get; init; }
}