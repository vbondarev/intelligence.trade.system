namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO агрегированного состояния стакана.</summary>
public sealed record MarketAnalysisOrderBookModel
{
    public DateTimeOffset CapturedAtUtc { get; init; }
    public decimal BestBidPrice { get; init; }
    public decimal BestAskPrice { get; init; }
    public decimal TotalBidVolumeTop5 { get; init; }
    public decimal TotalAskVolumeTop5 { get; init; }
    public decimal TotalBidVolumeTop10 { get; init; }
    public decimal TotalAskVolumeTop10 { get; init; }
    public decimal TotalBidVolumeTop20 { get; init; }
    public decimal TotalAskVolumeTop20 { get; init; }
    public decimal ImbalanceTop5 { get; init; }
    public decimal ImbalanceTop10 { get; init; }
    public decimal ImbalanceTop20 { get; init; }
    public required List<MarketAnalysisOrderBookLevelModel> TopBids { get; init; }
    public required List<MarketAnalysisOrderBookLevelModel> TopAsks { get; init; }
    public required List<MarketAnalysisLiquidityWallModel> BidWalls { get; init; }
    public required List<MarketAnalysisLiquidityWallModel> AskWalls { get; init; }
}
