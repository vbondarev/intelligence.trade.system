namespace Intelligence.TradeSystem.Domain.Portfolio;

public enum ReasonCode
{
    PortfolioDataIncomplete,
    PortfolioDataStale,
    InsufficientFreeCapital,
    GrossExposureLimitExceeded,
    ConcentrationLimitExceeded,
    RiskWithinLimits
}
