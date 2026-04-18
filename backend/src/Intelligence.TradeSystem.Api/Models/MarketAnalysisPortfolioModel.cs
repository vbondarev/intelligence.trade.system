namespace Intelligence.TradeSystem.Api.Models;

/// <summary>HTTP DTO состояния счёта и открытых позиций.</summary>
public sealed record MarketAnalysisPortfolioModel
{
    public decimal TotalEquityUsd { get; init; }
    public decimal AvailableBalanceUsd { get; init; }
    public decimal TotalWalletBalanceUsd { get; init; }
    public decimal TotalUnrealizedPnlUsd { get; init; }
    public required List<MarketAnalysisOpenPositionModel> OpenPositions { get; init; }
}
