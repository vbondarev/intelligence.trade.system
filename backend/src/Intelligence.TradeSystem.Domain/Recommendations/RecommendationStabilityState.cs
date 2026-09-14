namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Неизменяемое состояние подтверждения одного pending candidate.
/// </summary>
public sealed record RecommendationStabilityState
{
    public RecommendationStabilityState(
        RecommendationSemanticState pendingSemanticState,
        DateTimeOffset firstObservedAt,
        DateTimeOffset lastObservedAt,
        int consecutiveObservations)
    {
        ArgumentNullException.ThrowIfNull(pendingSemanticState);
        if (firstObservedAt == default)
            throw new ArgumentException("FirstObservedAt must be initialized.", nameof(firstObservedAt));
        if (lastObservedAt == default)
            throw new ArgumentException("LastObservedAt must be initialized.", nameof(lastObservedAt));
        if (lastObservedAt < firstObservedAt)
            throw new ArgumentException(
                "LastObservedAt cannot precede FirstObservedAt.",
                nameof(lastObservedAt));
        if (consecutiveObservations < 1)
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveObservations),
                consecutiveObservations,
                "Consecutive observations must be at least one.");

        PendingSemanticState = pendingSemanticState;
        FirstObservedAt = firstObservedAt;
        LastObservedAt = lastObservedAt;
        ConsecutiveObservations = consecutiveObservations;
    }

    public RecommendationSemanticState PendingSemanticState { get; }
    public RecommendationSemanticState SemanticState => PendingSemanticState;
    public DateTimeOffset FirstObservedAt { get; }
    public DateTimeOffset LastObservedAt { get; }
    public int ConsecutiveObservations { get; }
}
