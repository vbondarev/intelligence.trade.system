namespace Intelligence.TradeSystem.Domain.Decisions;

public static class ReasonCodeClassification
{
    public static bool IsPortfolioRiskReason(ReasonCode reason) => reason is
        ReasonCode.PortfolioDataIncomplete or
        ReasonCode.PortfolioDataStale or
        ReasonCode.InsufficientFreeCapital or
        ReasonCode.GrossExposureLimitExceeded or
        ReasonCode.ConcentrationLimitExceeded or
        ReasonCode.RiskWithinLimits;
}
