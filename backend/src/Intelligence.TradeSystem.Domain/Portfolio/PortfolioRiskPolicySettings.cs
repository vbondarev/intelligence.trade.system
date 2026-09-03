namespace Intelligence.TradeSystem.Domain.Portfolio;

public sealed record PortfolioRiskPolicySettings
{
    public PortfolioRiskPolicySettings(
        decimal minimumFreeCapitalPercent,
        decimal maximumGrossExposureToEquityPercent,
        decimal maximumPositionConcentrationPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumFreeCapitalPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumFreeCapitalPercent, 100m);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumGrossExposureToEquityPercent);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPositionConcentrationPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumPositionConcentrationPercent, 100m);

        MinimumFreeCapitalPercent = minimumFreeCapitalPercent;
        MaximumGrossExposureToEquityPercent = maximumGrossExposureToEquityPercent;
        MaximumPositionConcentrationPercent = maximumPositionConcentrationPercent;
    }

    public decimal MinimumFreeCapitalPercent { get; }
    public decimal MaximumGrossExposureToEquityPercent { get; }
    public decimal MaximumPositionConcentrationPercent { get; }
}
