using Intelligence.TradeSystem.Api.Contracts.V1.Portfolio;
using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Application.Realtime;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PortfolioMapper
{
    public static PortfolioResponse ToResponse(PortfolioReadSummary summary) =>
        new(
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
            summary.IsFresh,
            summary.CurrentPositionCount,
            summary.Exposures
                .Select(exposure => new PortfolioExposureResponse(
                    exposure.SettlementAsset.Value,
                    exposure.GrossExposure,
                    exposure.LongExposure,
                    exposure.ShortExposure))
                .ToArray())
        {
            FactualFreshness = new(
                ToWireStatus(summary.FactualFreshness.Status),
                summary.FactualFreshness.LastFullyReconciledAt),
            ValuationFreshness = new(
                MarketValuationMapper.ToWireStatus(summary.ValuationFreshness.Status),
                summary.ValuationFreshness.CalculatedAt),
        };

    private static FactualFreshnessStatusV1 ToWireStatus(FactualFreshnessStatus status) => status switch
    {
        FactualFreshnessStatus.Unknown => FactualFreshnessStatusV1.Unknown,
        FactualFreshnessStatus.Fresh => FactualFreshnessStatusV1.Fresh,
        FactualFreshnessStatus.Stale => FactualFreshnessStatusV1.Stale,
        FactualFreshnessStatus.Degraded => FactualFreshnessStatusV1.Degraded,
        _ => throw new NotSupportedException(
            $"Factual freshness status '{status}' is not mapped to a v1 wire contract."),
    };
}
