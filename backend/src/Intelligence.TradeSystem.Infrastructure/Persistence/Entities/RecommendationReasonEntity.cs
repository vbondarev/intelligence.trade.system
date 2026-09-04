using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class RecommendationReasonEntity
{
    public Guid RecommendationId { get; set; }
    public int Sequence { get; set; }
    public ReasonCode ReasonCode { get; set; }
}
