namespace Intelligence.TradeSystem.Api.Models.MarketAnalysis;

/// <summary>HTTP DTO состояния счёта и открытых позиций.</summary>
public sealed record PortfolioModel
{
    public decimal TotalEquityUsd { get; init; }
    public decimal AvailableBalanceUsd { get; init; }
    public decimal TotalWalletBalanceUsd { get; init; }
    public decimal TotalUnrealizedPnlUsd { get; init; }
    public required List<OpenPositionModel> OpenPositions { get; init; }
}
