using Intelligence.TradeSystem.Api.Contracts.V1.Common;
using Intelligence.TradeSystem.Api.Contracts.V1.Positions;
using Intelligence.TradeSystem.Api.Serialization;
using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Api.Mappers;

internal static class PositionTimelineMapper
{
    public static CursorPage<PositionTimelineItemResponse> ToResponse(PositionTimelinePage page) =>
        new(
            page.Items.Select(ToResponse).ToArray(),
            page.NextCursor is { } nextCursor
                ? PositionTimelineCursorCodec.Encode(nextCursor)
                : null,
            page.HasMore);

    private static PositionTimelineItemResponse ToResponse(PositionTimelineItem item) => item.Kind switch
    {
        PositionTimelineItemKind.PositionChange => new(
            PositionTimelineItemTypeV1.PositionChange,
            item.OccurredAt,
            ToResponse(item.PositionChange
                ?? throw new InvalidOperationException(
                    "A position change timeline item must contain a change.")),
            null,
            null),
        PositionTimelineItemKind.Evaluation => new(
            PositionTimelineItemTypeV1.Evaluation,
            item.OccurredAt,
            null,
            ToResponse(item.Evaluation
                ?? throw new InvalidOperationException(
                    "An evaluation timeline item must contain an evaluation.")),
            null),
        PositionTimelineItemKind.Recommendation => new(
            PositionTimelineItemTypeV1.Recommendation,
            item.OccurredAt,
            null,
            null,
            ToResponse(item.Recommendation
                ?? throw new InvalidOperationException(
                    "A recommendation timeline item must contain a recommendation."))),
        _ => throw new InvalidOperationException(
            $"Unsupported timeline item kind '{item.Kind}'."),
    };

    private static PositionTimelinePositionChangeResponse ToResponse(
        PositionTimelinePositionChange change) =>
        new(
            change.Sequence,
            ToWire(change.Kind),
            ToWire(change.Cause),
            PositionV1EnumMapper.ToWire(change.TrackingStateAfter),
            change.Before is { } before ? ToResponse(before) : null,
            ToResponse(change.After));

    private static PositionTimelinePositionSnapshotResponse ToResponse(
        PositionTimelinePositionSnapshot snapshot) =>
        new(
            snapshot.Size,
            snapshot.AverageEntryPrice,
            snapshot.PositionValue,
            snapshot.Leverage,
            snapshot.MarkPrice,
            snapshot.BreakEvenPrice,
            snapshot.LiquidationPrice,
            snapshot.UnrealizedPnl,
            snapshot.TakeProfit,
            snapshot.StopLoss,
            snapshot.TrailingStop);

    private static PositionTimelineEvaluationResponse ToResponse(
        PositionTimelineEvaluation evaluation) =>
        new(
            evaluation.Id.Value,
            evaluation.EvaluatedAt,
            evaluation.ValidUntil,
            evaluation.RuleVersion.Value,
            evaluation.IsLegacy,
            new(
                PositionV1EnumMapper.ToWire(evaluation.DataQuality.Market),
                PositionV1EnumMapper.ToWire(evaluation.DataQuality.Portfolio),
                PositionV1EnumMapper.ToWire(evaluation.DataQuality.Overall),
                PositionV1EnumMapper.ToWire(evaluation.DataQuality.SafetyState)),
            PositionV1EnumMapper.ToWire(evaluation.PortfolioRiskDecision),
            evaluation.ReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray());

    private static PositionTimelineRecommendationResponse ToResponse(
        PositionTimelineRecommendation recommendation) =>
        new(
            recommendation.Id.Value,
            recommendation.AssessmentId.Value,
            recommendation.CreatedAt,
            recommendation.ValidUntil,
            PositionV1EnumMapper.ToWire(recommendation.Status),
            PositionV1EnumMapper.ToWire(recommendation.Action),
            recommendation.Confidence,
            recommendation.Priority is { } priority
                ? PositionV1EnumMapper.ToWire(priority)
                : null,
            PositionV1EnumMapper.ToWire(recommendation.AddDecision),
            recommendation.ReasonCodes.Select(PositionV1EnumMapper.ToWire).ToArray(),
            recommendation.IsLegacy);

    private static PositionChangeKindV1 ToWire(PositionChangeKind value) => value switch
    {
        PositionChangeKind.New => PositionChangeKindV1.New,
        PositionChangeKind.Updated => PositionChangeKindV1.Updated,
        PositionChangeKind.Increased => PositionChangeKindV1.Increased,
        PositionChangeKind.Reduced => PositionChangeKindV1.Reduced,
        PositionChangeKind.Closed => PositionChangeKindV1.Closed,
        PositionChangeKind.MarkedUnknown => PositionChangeKindV1.MarkedUnknown,
        PositionChangeKind.MarkedStale => PositionChangeKindV1.MarkedStale,
        PositionChangeKind.Recovered => PositionChangeKindV1.Recovered,
        _ => throw new NotSupportedException(
            $"Position change kind '{value}' is not mapped to v1."),
    };

    private static PositionChangeCauseV1 ToWire(PositionChangeCause value) => value switch
    {
        PositionChangeCause.InitialObservation => PositionChangeCauseV1.InitialObservation,
        PositionChangeCause.ExchangeObservation => PositionChangeCauseV1.ExchangeObservation,
        PositionChangeCause.MissingFromCompleteObservation =>
            PositionChangeCauseV1.MissingFromCompleteObservation,
        PositionChangeCause.PositionsObservationFailed =>
            PositionChangeCauseV1.PositionsObservationFailed,
        PositionChangeCause.PartialObservation => PositionChangeCauseV1.PartialObservation,
        PositionChangeCause.FreshnessExpired => PositionChangeCauseV1.FreshnessExpired,
        PositionChangeCause.ObservationRestored => PositionChangeCauseV1.ObservationRestored,
        _ => throw new NotSupportedException(
            $"Position change cause '{value}' is not mapped to v1."),
    };
}
