namespace Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;

/// <summary>Сводка капитала account-scoped снимка портфеля.</summary>
public sealed record PortfolioCapitalResponse(
    decimal? TotalEquity,
    decimal? AvailableCapital,
    decimal? TotalWalletBalance,
    DateTimeOffset? ObservedAt);
