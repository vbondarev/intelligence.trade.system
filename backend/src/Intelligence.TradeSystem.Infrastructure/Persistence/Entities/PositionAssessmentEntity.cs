using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class PositionAssessmentEntity
{
    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public Guid ExchangeAccountId { get; set; }
    public string InstrumentId { get; set; } = null!;
    public DateTimeOffset PositionObservedAt { get; set; }
    public DateTimeOffset PortfolioCalculatedAt { get; set; }
    public DateTimeOffset MarketCapturedAt { get; set; }
    public string RuleVersion { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public RiskIncreaseDecision PortfolioRiskDecision { get; set; }
}
