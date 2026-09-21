namespace Intelligence.TradeSystem.Api.Contracts.V1.Positions;

public sealed record PositionTimelineItemResponse(
    PositionTimelineItemTypeV1 Type,
    DateTimeOffset OccurredAt,
    PositionTimelinePositionChangeResponse? PositionChange,
    PositionTimelineEvaluationResponse? Evaluation,
    PositionTimelineRecommendationResponse? Recommendation);

public sealed record PositionTimelinePositionChangeResponse(
    int Sequence,
    PositionChangeKindV1 Kind,
    PositionChangeCauseV1 Cause,
    PositionTrackingStateV1 TrackingStateAfter,
    PositionTimelinePositionSnapshotResponse? Before,
    PositionTimelinePositionSnapshotResponse After);

public sealed record PositionTimelinePositionSnapshotResponse(
    decimal Size,
    decimal? AverageEntryPrice,
    decimal? PositionValue,
    decimal? Leverage,
    decimal? MarkPrice,
    decimal? BreakEvenPrice,
    decimal? LiquidationPrice,
    decimal? UnrealizedPnl,
    decimal? TakeProfit,
    decimal? StopLoss,
    decimal? TrailingStop);

public sealed record PositionTimelineEvaluationResponse(
    Guid Id,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ValidUntil,
    string RuleVersion,
    bool IsLegacy,
    PositionAssessmentDataQualityResponse DataQuality,
    RiskIncreaseDecisionV1 PortfolioRiskDecision,
    IReadOnlyList<ReasonCodeV1> ReasonCodes);

public sealed record PositionTimelineRecommendationResponse(
    Guid Id,
    Guid AssessmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidUntil,
    RecommendationStatusV1 Status,
    PositionActionV1 Action,
    decimal? Confidence,
    RecommendationPriorityV1? Priority,
    AddDecisionV1 AddDecision,
    IReadOnlyList<ReasonCodeV1> ReasonCodes,
    bool IsLegacy);

public enum PositionTimelineItemTypeV1
{
    PositionChange,
    Evaluation,
    Recommendation,
}

public enum PositionChangeKindV1
{
    New,
    Updated,
    Increased,
    Reduced,
    Closed,
    MarkedUnknown,
    MarkedStale,
    Recovered,
}

public enum PositionChangeCauseV1
{
    InitialObservation,
    ExchangeObservation,
    MissingFromCompleteObservation,
    PositionsObservationFailed,
    PartialObservation,
    FreshnessExpired,
    ObservationRestored,
}
