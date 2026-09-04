namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PortfolioStateEntity
{
    public ICollection<PortfolioPositionStateEntity> Positions { get; } = [];

    public long Id { get; set; }
    public Guid ExchangeAccountId { get; set; }
    public decimal? TotalEquity { get; set; }
    public decimal? AvailableCapital { get; set; }
    public DateTimeOffset? CapitalObservedAt { get; set; }
    public decimal? TotalWalletBalance { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public TimeSpan StaleAfter { get; set; }
    public decimal? GrossExposure { get; set; }
    public decimal? LongExposure { get; set; }
    public decimal? ShortExposure { get; set; }
    public decimal? NetExposure { get; set; }
    public decimal? TotalUnrealizedPnl { get; set; }
    public decimal? UsedCapital { get; set; }
    public decimal? FreeCapital { get; set; }
    public decimal? FreeCapitalPercent { get; set; }
    public decimal? GrossExposureToEquityPercent { get; set; }
    public decimal? LargestPositionConcentrationPercent { get; set; }
    public Guid? LargestPositionId { get; set; }
    public bool IsComplete { get; set; }
    public bool IsFresh { get; set; }
}
