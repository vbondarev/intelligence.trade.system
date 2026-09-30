using Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;
using Intelligence.TradeSystem.Application.Portfolio.Read;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PortfolioMapper
{
    public static PortfolioResponse ToResponse(PortfolioReadSummary summary) => new(
        summary.ExchangeAccountId.Value,
        summary.CalculatedAt,
        new PortfolioCapitalResponse(
            summary.TotalEquity,
            summary.AvailableCapital,
            summary.TotalWalletBalance,
            summary.CapitalObservedAt),
        summary.GrossExposure,
        summary.LongExposure,
        summary.ShortExposure,
        summary.NetExposure,
        summary.TotalUnrealizedPnl,
        summary.UsedCapital,
        summary.FreeCapital,
        summary.FreeCapitalPercent,
        summary.GrossExposureToEquityPercent,
        summary.LargestPositionConcentrationPercent,
        summary.LargestPositionId?.Value,
        summary.PositionsFullyReconciled,
        summary.IsComplete,
        summary.IsFresh);
}
