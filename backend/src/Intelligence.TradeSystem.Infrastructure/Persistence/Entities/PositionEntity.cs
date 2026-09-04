using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PositionEntity
{
    public Guid Id { get; set; }
    public Guid ExchangeAccountId { get; set; }
    public string InstrumentId { get; set; } = null!;
    public PositionSide PositionSide { get; set; }
    public int PositionIdx { get; set; }
    public MarketCategory MarketCategory { get; set; }
    public decimal Size { get; set; }
    public decimal? AverageEntryPrice { get; set; }
    public decimal? PositionValue { get; set; }
    public decimal? Leverage { get; set; }
    public decimal? MarkPrice { get; set; }
    public decimal? BreakEvenPrice { get; set; }
    public decimal? LiquidationPrice { get; set; }
    public decimal? UnrealizedPnl { get; set; }
    public decimal? TakeProfit { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TrailingStop { get; set; }
    public DateTimeOffset FirstDetectedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public PositionTrackingState TrackingState { get; set; }
    public long Version { get; set; }
}
