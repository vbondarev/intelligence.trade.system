namespace Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

public sealed class RecommendationStabilityStateEntity
{
    public Guid PositionId { get; set; }
    public Guid StateId { get; set; }
    public Guid BaselineRecommendationId { get; set; }
    public string SemanticStateJson { get; set; } = null!;
    public DateTimeOffset FirstObservedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public int ConsecutiveObservations { get; set; }
    public long Version { get; set; }
}
