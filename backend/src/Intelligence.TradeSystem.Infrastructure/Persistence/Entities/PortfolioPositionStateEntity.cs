using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PortfolioPositionStateEntity
{
    public long PortfolioStateId { get; set; }
    public int Sequence { get; set; }
    public Guid PositionId { get; set; }
    public Guid ExchangeAccountId { get; set; }
    public string InstrumentId { get; set; } = null!;
    public PositionSide PositionSide { get; set; }
    public int PositionIdx { get; set; }
    public MarketCategory MarketCategory { get; set; }
    public PositionTrackingState TrackingState { get; set; }
    public decimal Size { get; set; }
    public decimal? PositionValue { get; set; }
    public decimal? UnrealizedPnl { get; set; }
    public decimal? AverageEntryPrice { get; set; }
    public decimal? MarkPrice { get; set; }
    public decimal? LiquidationPrice { get; set; }
    public decimal? Leverage { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
}
