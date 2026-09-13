using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

public sealed record AdditionalPositionCapacityResult(
    decimal? MaximumPositionValue,
    decimal? MaximumQuantity,
    IReadOnlyList<ReasonCode> LimitingReasons);

/// <summary>
/// Единственный источник истины для расчёта maximum additional position size.
/// </summary>
public static class AdditionalPositionCapacityCalculator
{
    public static AdditionalPositionCapacityResult Calculate(
        PositionAssessmentPortfolioRiskContext portfolioRisk,
        decimal? currentPrice,
        AddAllowedPolicyLimits limits)
    {
        ArgumentNullException.ThrowIfNull(portfolioRisk);
        ArgumentNullException.ThrowIfNull(limits);

        if (portfolioRisk.TotalEquity is not > 0m ||
            portfolioRisk.AvailableCapital is not >= 0m ||
            portfolioRisk.CurrentPositionValue is not >= 0m ||
            !portfolioRisk.IsComplete ||
            !portfolioRisk.IsFresh ||
            portfolioRisk.GrossExposureToEquityPercent is null ||
            portfolioRisk.MinimumFreeCapitalPercent is null ||
            portfolioRisk.MaximumGrossExposureToEquityPercent is null ||
            portfolioRisk.MaximumPositionConcentrationPercent is null)
        {
            return new(null, null, [ReasonCode.AddMaximumSizeUnavailable]);
        }

        var equity = portfolioRisk.TotalEquity.Value;
        var available = portfolioRisk.AvailableCapital.Value;
        var freeCapitalRoom =
            available - equity * portfolioRisk.MinimumFreeCapitalPercent.Value / 100m;
        var grossExposureRoom =
            equity * portfolioRisk.MaximumGrossExposureToEquityPercent.Value / 100m -
            equity * portfolioRisk.GrossExposureToEquityPercent.Value / 100m;
        var maximumConcentration = portfolioRisk.MaximumPositionConcentrationPercent.Value / 100m;
        var currentGrossExposure =
            equity * portfolioRisk.GrossExposureToEquityPercent.Value / 100m;
        var positionConcentrationRoom = maximumConcentration >= 1m
            ? decimal.MaxValue
            : (maximumConcentration * currentGrossExposure -
               portfolioRisk.CurrentPositionValue.Value) / (1m - maximumConcentration);
        var policyRelativeRoom =
            equity * limits.MaximumAdditionalPositionPercentOfEquity / 100m;
        var policyAvailableRoom =
            available * limits.MaximumAdditionalAvailableCapitalPercent / 100m;
        var reasons = new List<ReasonCode>();

        if (freeCapitalRoom <= 0m ||
            grossExposureRoom <= 0m ||
            positionConcentrationRoom <= 0m)
            reasons.Add(ReasonCode.AddBlockedByPortfolioRisk);

        var maximum = Math.Max(
            0m,
            Math.Min(
                freeCapitalRoom,
                Math.Min(
                    grossExposureRoom,
                    Math.Min(positionConcentrationRoom, Math.Min(policyRelativeRoom, policyAvailableRoom)))));
        var maximumQuantity = maximum > 0m && currentPrice is > 0m
            ? (decimal?)(maximum / currentPrice.Value)
            : null;

        return new(maximum, maximumQuantity, reasons.Distinct().ToArray());
    }
}


