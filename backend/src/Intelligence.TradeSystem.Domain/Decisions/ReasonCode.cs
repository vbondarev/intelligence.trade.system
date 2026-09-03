namespace Intelligence.TradeSystem.Domain.Decisions;

public enum ReasonCode
{
    PortfolioDataIncomplete,
    PortfolioDataStale,
    InsufficientFreeCapital,
    GrossExposureLimitExceeded,
    ConcentrationLimitExceeded,
    RiskWithinLimits
}
