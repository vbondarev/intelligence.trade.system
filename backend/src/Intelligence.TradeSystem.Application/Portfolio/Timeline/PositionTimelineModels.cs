using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Portfolio.Timeline;

public enum PositionTimelineItemKind
{
    PositionChange,
    Evaluation,
    Recommendation,
}

public sealed class PositionTimelineQuery
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;

    public PositionTimelineQuery(
        PositionId positionId,
        int pageSize,
        IEnumerable<PositionTimelineItemKind> selectedKinds,
        PositionTimelineCursor? cursor)
    {
        if (positionId == default)
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
        if (pageSize is < MinPageSize or > MaxPageSize)
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"PageSize must be between {MinPageSize} and {MaxPageSize}.");
        ArgumentNullException.ThrowIfNull(selectedKinds);

        var kinds = selectedKinds.ToArray();
        if (kinds.Length == 0)
            throw new ArgumentException("At least one timeline item kind must be selected.", nameof(selectedKinds));
        if (kinds.Any(kind => !Enum.IsDefined(kind)))
            throw new ArgumentOutOfRangeException(
                nameof(selectedKinds),
                "Timeline item kinds must be defined.");
        if (kinds.Distinct().Count() != kinds.Length)
            throw new ArgumentException("Timeline item kinds cannot contain duplicates.", nameof(selectedKinds));
        if (cursor is { } value && !kinds.Contains(value.Kind))
            throw new ArgumentException(
                "The cursor kind must be included in the selected timeline item kinds.",
                nameof(cursor));

        PositionId = positionId;
        PageSize = pageSize;
        SelectedKinds = Array.AsReadOnly(kinds);
        Cursor = cursor;
    }

    public PositionId PositionId { get; }
    public int PageSize { get; }
    public IReadOnlyList<PositionTimelineItemKind> SelectedKinds { get; }
    public PositionTimelineCursor? Cursor { get; }

    public bool Includes(PositionTimelineItemKind kind) => SelectedKinds.Contains(kind);
}

public readonly record struct PositionTimelineCursor
{
    public PositionTimelineCursor(
        DateTimeOffset occurredAt,
        PositionTimelineItemKind kind,
        Guid? sourceId,
        int? positionChangeSequence)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Timeline item kind must be defined.");

        switch (kind)
        {
            case PositionTimelineItemKind.PositionChange when
                sourceId is not null || positionChangeSequence is not > 0:
                throw new ArgumentException(
                    "A position-change cursor requires a positive sequence and no source ID.");
            case PositionTimelineItemKind.Evaluation or PositionTimelineItemKind.Recommendation when
                sourceId is not { } source || source == Guid.Empty || positionChangeSequence is not null:
                throw new ArgumentException(
                    "An evaluation or recommendation cursor requires a source ID and no position-change sequence.");
        }

        OccurredAt = occurredAt;
        Kind = kind;
        SourceId = sourceId;
        PositionChangeSequence = positionChangeSequence;
    }

    public DateTimeOffset OccurredAt { get; }
    public PositionTimelineItemKind Kind { get; }
    public Guid? SourceId { get; }
    public int? PositionChangeSequence { get; }
}

public sealed record PositionTimelinePage(
    IReadOnlyList<PositionTimelineItem> Items,
    PositionTimelineCursor? NextCursor,
    bool HasMore);

public sealed record PositionTimelineCandidates(
    IReadOnlyList<PositionTimelineItem> PositionChanges,
    IReadOnlyList<PositionTimelineItem> Evaluations,
    IReadOnlyList<PositionTimelineItem> Recommendations);

public sealed record PositionTimelineItem(
    PositionTimelineItemKind Kind,
    DateTimeOffset OccurredAt,
    PositionTimelinePositionChange? PositionChange,
    PositionTimelineEvaluation? Evaluation,
    PositionTimelineRecommendation? Recommendation)
{
    public static PositionTimelineItem ForPositionChange(
        DateTimeOffset occurredAt,
        PositionTimelinePositionChange change) =>
        new(PositionTimelineItemKind.PositionChange, occurredAt, change, null, null);

    public static PositionTimelineItem ForEvaluation(
        PositionTimelineEvaluation evaluation) =>
        new(PositionTimelineItemKind.Evaluation, evaluation.EvaluatedAt, null, evaluation, null);

    public static PositionTimelineItem ForRecommendation(
        PositionTimelineRecommendation recommendation) =>
        new(PositionTimelineItemKind.Recommendation, recommendation.CreatedAt, null, null, recommendation);

    public PositionTimelineCursor ToCursor() => Kind switch
    {
        PositionTimelineItemKind.PositionChange => new(
            OccurredAt,
            Kind,
            null,
            PositionChange!.Sequence),
        PositionTimelineItemKind.Evaluation => new(
            OccurredAt,
            Kind,
            Evaluation!.Id.Value,
            null),
        PositionTimelineItemKind.Recommendation => new(
            OccurredAt,
            Kind,
            Recommendation!.Id.Value,
            null),
        _ => throw new InvalidOperationException($"Unsupported timeline item kind '{Kind}'."),
    };
}

public sealed record PositionTimelinePositionChange(
    int Sequence,
    PositionChangeKind Kind,
    PositionChangeCause Cause,
    PositionTrackingState TrackingStateAfter,
    PositionTimelinePositionSnapshot? Before,
    PositionTimelinePositionSnapshot After);

public sealed record PositionTimelinePositionSnapshot(
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

public sealed record PositionTimelineEvaluation(
    PositionAssessmentId Id,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset ValidUntil,
    RuleVersion RuleVersion,
    bool IsLegacy,
    PositionAssessmentDataQualityContext DataQuality,
    RiskIncreaseDecision PortfolioRiskDecision,
    IReadOnlyList<ReasonCode> ReasonCodes);

public sealed record PositionTimelineRecommendation(
    RecommendationId Id,
    PositionAssessmentId AssessmentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ValidUntil,
    RecommendationStatus Status,
    PositionAction Action,
    decimal? Confidence,
    RecommendationPriority? Priority,
    AddDecision AddDecision,
    IReadOnlyList<ReasonCode> ReasonCodes,
    bool IsLegacy);
