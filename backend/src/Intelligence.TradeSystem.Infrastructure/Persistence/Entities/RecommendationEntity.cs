using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class RecommendationEntity
{
    public Guid Id { get; set; }
    public Guid AssessmentId { get; set; }
    public Guid PositionId { get; set; }
    public PositionAction RecommendedAction { get; set; }
    public AddDecision AddDecision { get; set; }
    public string PolicyVersion { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public RecommendationStatus Status { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }
    public Guid? SupersededByRecommendationId { get; set; }
}
