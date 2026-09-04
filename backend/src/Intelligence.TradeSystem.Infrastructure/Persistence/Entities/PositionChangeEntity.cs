using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PositionChangeEntity
{
    public Guid PositionId { get; set; }
    public int Sequence { get; set; }
    public PositionChangeKind Kind { get; set; }
    public PositionChangeCause Cause { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public PositionTrackingState TrackingStateAfter { get; set; }

    public decimal? BeforeSize { get; set; }
    public decimal? BeforeAverageEntryPrice { get; set; }
    public decimal? BeforePositionValue { get; set; }
    public decimal? BeforeLeverage { get; set; }
    public decimal? BeforeMarkPrice { get; set; }
    public decimal? BeforeBreakEvenPrice { get; set; }
    public decimal? BeforeLiquidationPrice { get; set; }
    public decimal? BeforeUnrealizedPnl { get; set; }
    public decimal? BeforeTakeProfit { get; set; }
    public decimal? BeforeStopLoss { get; set; }
    public decimal? BeforeTrailingStop { get; set; }

    public decimal AfterSize { get; set; }
    public decimal? AfterAverageEntryPrice { get; set; }
    public decimal? AfterPositionValue { get; set; }
    public decimal? AfterLeverage { get; set; }
    public decimal? AfterMarkPrice { get; set; }
    public decimal? AfterBreakEvenPrice { get; set; }
    public decimal? AfterLiquidationPrice { get; set; }
    public decimal? AfterUnrealizedPnl { get; set; }
    public decimal? AfterTakeProfit { get; set; }
    public decimal? AfterStopLoss { get; set; }
    public decimal? AfterTrailingStop { get; set; }
}
