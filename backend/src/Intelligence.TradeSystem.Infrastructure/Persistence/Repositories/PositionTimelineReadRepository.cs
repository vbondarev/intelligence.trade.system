using Intelligence.TradeSystem.Application.Portfolio.Timeline;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;
using Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;
using Microsoft.EntityFrameworkCore;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Repositories;

public sealed class PositionTimelineReadRepository(TradeSystemDbContext dbContext)
    : IPositionTimelineReadStore
{
    public async Task<PositionTimelineCandidates?> ReadCandidatesAsync(
        UserId userId,
        PositionTimelineQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        ArgumentNullException.ThrowIfNull(query);

        var ownsPosition = await dbContext.Positions
            .AsNoTracking()
            .AnyAsync(
                position =>
                    position.Id == query.PositionId.Value &&
                    dbContext.ExchangeAccounts.Any(account =>
                        account.Id == position.ExchangeAccountId &&
                        account.UserId == userId.Value),
                cancellationToken)
            .ConfigureAwait(false);
        if (!ownsPosition)
            return null;

        var limit = query.PageSize + 1;
        var positionChanges = query.Includes(PositionTimelineItemKind.PositionChange)
            ? await ReadPositionChangesAsync(userId, query, limit, cancellationToken).ConfigureAwait(false)
            : [];
        var evaluations = query.Includes(PositionTimelineItemKind.Evaluation)
            ? await ReadEvaluationsAsync(userId, query, limit, cancellationToken).ConfigureAwait(false)
            : [];
        var recommendations = query.Includes(PositionTimelineItemKind.Recommendation)
            ? await ReadRecommendationsAsync(userId, query, limit, cancellationToken).ConfigureAwait(false)
            : [];

        return new PositionTimelineCandidates(positionChanges, evaluations, recommendations);
    }

    private async Task<IReadOnlyList<PositionTimelineItem>> ReadPositionChangesAsync(
        UserId userId,
        PositionTimelineQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyPositionChangeCursor(
                dbContext.PositionChanges
                    .AsNoTracking()
                    .Where(change =>
                        change.PositionId == query.PositionId.Value &&
                        dbContext.Positions.Any(position =>
                            position.Id == change.PositionId &&
                            dbContext.ExchangeAccounts.Any(account =>
                                account.Id == position.ExchangeAccountId &&
                                account.UserId == userId.Value))),
                query.Cursor)
            .OrderByDescending(change => change.OccurredAt)
            .ThenByDescending(change => change.Sequence)
            .Select(change => new PositionChangeRow(
                change.Sequence,
                change.Kind,
                change.Cause,
                change.OccurredAt,
                change.TrackingStateAfter,
                change.BeforeSize,
                change.BeforeAverageEntryPrice,
                change.BeforePositionValue,
                change.BeforeLeverage,
                change.BeforeMarkPrice,
                change.BeforeBreakEvenPrice,
                change.BeforeLiquidationPrice,
                change.BeforeUnrealizedPnl,
                change.BeforeTakeProfit,
                change.BeforeStopLoss,
                change.BeforeTrailingStop,
                change.AfterSize,
                change.AfterAverageEntryPrice,
                change.AfterPositionValue,
                change.AfterLeverage,
                change.AfterMarkPrice,
                change.AfterBreakEvenPrice,
                change.AfterLiquidationPrice,
                change.AfterUnrealizedPnl,
                change.AfterTakeProfit,
                change.AfterStopLoss,
                change.AfterTrailingStop))
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToTimelineItem).ToArray();
    }

    private async Task<IReadOnlyList<PositionTimelineItem>> ReadEvaluationsAsync(
        UserId userId,
        PositionTimelineQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyEvaluationCursor(
                dbContext.PositionAssessments
                    .AsNoTracking()
                    .Where(assessment =>
                        assessment.PositionId == query.PositionId.Value &&
                        dbContext.Positions.Any(position =>
                            position.Id == assessment.PositionId &&
                            position.ExchangeAccountId == assessment.ExchangeAccountId &&
                            dbContext.ExchangeAccounts.Any(account =>
                                account.Id == position.ExchangeAccountId &&
                                account.UserId == userId.Value))),
                query.Cursor)
            .OrderByDescending(assessment => assessment.CreatedAt)
            .ThenByDescending(assessment => assessment.Id)
            .Select(assessment => new EvaluationRow(
                assessment.Id,
                assessment.CreatedAt,
                assessment.ValidUntil,
                assessment.RuleVersion,
                assessment.ResultJson,
                assessment.PortfolioRiskDecision))
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var reasons = await ReadEvaluationReasonsAsync(
                rows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(row => ToTimelineItem(row, reasons))
            .ToArray();
    }

    private async Task<IReadOnlyList<PositionTimelineItem>> ReadRecommendationsAsync(
        UserId userId,
        PositionTimelineQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await ApplyRecommendationCursor(
                dbContext.Recommendations
                    .AsNoTracking()
                    .Where(recommendation =>
                        recommendation.PositionId == query.PositionId.Value &&
                        dbContext.Positions.Any(position =>
                            position.Id == recommendation.PositionId &&
                            dbContext.ExchangeAccounts.Any(account =>
                                account.Id == position.ExchangeAccountId &&
                                account.UserId == userId.Value))),
                query.Cursor)
            .OrderByDescending(recommendation => recommendation.CreatedAt)
            .ThenByDescending(recommendation => recommendation.Id)
            .Select(recommendation => new RecommendationRow(
                recommendation.Id,
                recommendation.AssessmentId,
                recommendation.CreatedAt,
                recommendation.ValidUntil,
                recommendation.Status,
                recommendation.RecommendedAction,
                recommendation.Confidence,
                recommendation.Priority,
                recommendation.AddDecision,
                recommendation.PolicyHash,
                recommendation.DecisionContextJson))
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var reasons = await ReadRecommendationReasonsAsync(
                rows.Select(row => row.Id).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(row => ToTimelineItem(row, reasons))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReasonCode>>> ReadEvaluationReasonsAsync(
        Guid[] assessmentIds,
        CancellationToken cancellationToken)
    {
        if (assessmentIds.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<ReasonCode>>();

        var rows = await dbContext.PositionAssessmentReasons
            .AsNoTracking()
            .Where(reason => assessmentIds.Contains(reason.PositionAssessmentId))
            .OrderBy(reason => reason.PositionAssessmentId)
            .ThenBy(reason => reason.Sequence)
            .Select(reason => new ReasonRow(reason.PositionAssessmentId, reason.ReasonCode))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.SourceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReasonCode>)group.Select(row => row.ReasonCode).ToArray());
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReasonCode>>> ReadRecommendationReasonsAsync(
        Guid[] recommendationIds,
        CancellationToken cancellationToken)
    {
        if (recommendationIds.Length == 0)
            return new Dictionary<Guid, IReadOnlyList<ReasonCode>>();

        var rows = await dbContext.RecommendationReasons
            .AsNoTracking()
            .Where(reason => recommendationIds.Contains(reason.RecommendationId))
            .OrderBy(reason => reason.RecommendationId)
            .ThenBy(reason => reason.Sequence)
            .Select(reason => new ReasonRow(reason.RecommendationId, reason.ReasonCode))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.SourceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ReasonCode>)group.Select(row => row.ReasonCode).ToArray());
    }

    private static IQueryable<PositionChangeEntity> ApplyPositionChangeCursor(
        IQueryable<PositionChangeEntity> source,
        PositionTimelineCursor? cursor)
    {
        if (cursor is not { } value)
            return source;

        var cursorRank = GetTypeRank(value.Kind);
        var cursorSequence = value.PositionChangeSequence ?? 0;
        return source.Where(change =>
            change.OccurredAt < value.OccurredAt ||
            (change.OccurredAt == value.OccurredAt &&
             (PositionChangeRank < cursorRank ||
              (PositionChangeRank == cursorRank && change.Sequence < cursorSequence))));
    }

    private static IQueryable<PositionAssessmentEntity> ApplyEvaluationCursor(
        IQueryable<PositionAssessmentEntity> source,
        PositionTimelineCursor? cursor)
    {
        if (cursor is not { } value)
            return source;

        var cursorRank = GetTypeRank(value.Kind);
        var cursorId = value.SourceId ?? Guid.Empty;
        return source.Where(assessment =>
            assessment.CreatedAt < value.OccurredAt ||
            (assessment.CreatedAt == value.OccurredAt &&
             (EvaluationRank < cursorRank ||
              (EvaluationRank == cursorRank &&
               assessment.Id.CompareTo(cursorId) < 0))));
    }

    private static IQueryable<RecommendationEntity> ApplyRecommendationCursor(
        IQueryable<RecommendationEntity> source,
        PositionTimelineCursor? cursor)
    {
        if (cursor is not { } value)
            return source;

        var cursorRank = GetTypeRank(value.Kind);
        var cursorId = value.SourceId ?? Guid.Empty;
        return source.Where(recommendation =>
            recommendation.CreatedAt < value.OccurredAt ||
            (recommendation.CreatedAt == value.OccurredAt &&
             (RecommendationRank < cursorRank ||
              (RecommendationRank == cursorRank &&
               recommendation.Id.CompareTo(cursorId) < 0))));
    }

    private static PositionTimelineItem ToTimelineItem(PositionChangeRow row)
    {
        var before = row.BeforeSize is { } beforeSize
            ? new PositionTimelinePositionSnapshot(
                beforeSize,
                row.BeforeAverageEntryPrice,
                row.BeforePositionValue,
                row.BeforeLeverage,
                row.BeforeMarkPrice,
                row.BeforeBreakEvenPrice,
                row.BeforeLiquidationPrice,
                row.BeforeUnrealizedPnl,
                row.BeforeTakeProfit,
                row.BeforeStopLoss,
                row.BeforeTrailingStop)
            : null;
        var after = new PositionTimelinePositionSnapshot(
            row.AfterSize,
            row.AfterAverageEntryPrice,
            row.AfterPositionValue,
            row.AfterLeverage,
            row.AfterMarkPrice,
            row.AfterBreakEvenPrice,
            row.AfterLiquidationPrice,
            row.AfterUnrealizedPnl,
            row.AfterTakeProfit,
            row.AfterStopLoss,
            row.AfterTrailingStop);

        return PositionTimelineItem.ForPositionChange(
            PersistenceDateTime.ToUtc(row.OccurredAt),
            new PositionTimelinePositionChange(
                row.Sequence,
                row.Kind,
                row.Cause,
                row.TrackingStateAfter,
                before,
                after));
    }

    private static PositionTimelineItem ToTimelineItem(
        EvaluationRow row,
        IReadOnlyDictionary<Guid, IReadOnlyList<ReasonCode>> reasons)
    {
        var data = PositionAssessmentMapper.ToTimelineData(
            row.Id,
            row.ResultJson,
            row.PortfolioRiskDecision);
        return PositionTimelineItem.ForEvaluation(
            new PositionTimelineEvaluation(
                PositionAssessmentId.FromGuid(row.Id),
                PersistenceDateTime.ToUtc(row.CreatedAt),
                PersistenceDateTime.ToUtc(row.ValidUntil),
                RuleVersion.From(row.RuleVersion),
                data.IsLegacy,
                data.DataQuality,
                row.PortfolioRiskDecision,
                reasons.TryGetValue(row.Id, out var sourceReasons) ? sourceReasons : []));
    }

    private static PositionTimelineItem ToTimelineItem(
        RecommendationRow row,
        IReadOnlyDictionary<Guid, IReadOnlyList<ReasonCode>> reasons)
    {
        var isLegacy = RecommendationMapper.IsLegacy(
            row.PolicyHash,
            row.Confidence,
            row.Priority,
            row.DecisionContextJson);
        return PositionTimelineItem.ForRecommendation(
            new PositionTimelineRecommendation(
                RecommendationId.FromGuid(row.Id),
                PositionAssessmentId.FromGuid(row.AssessmentId),
                PersistenceDateTime.ToUtc(row.CreatedAt),
                PersistenceDateTime.ToUtc(row.ValidUntil),
                row.Status,
                row.Action,
                row.Confidence,
                row.Priority,
                row.AddDecision,
                reasons.TryGetValue(row.Id, out var sourceReasons) ? sourceReasons : [],
                isLegacy));
    }

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
    }

    private static int GetTypeRank(PositionTimelineItemKind kind) => kind switch
    {
        PositionTimelineItemKind.Recommendation => RecommendationRank,
        PositionTimelineItemKind.Evaluation => EvaluationRank,
        PositionTimelineItemKind.PositionChange => PositionChangeRank,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Timeline item kind must be defined."),
    };

    private const int RecommendationRank = 30;
    private const int EvaluationRank = 20;
    private const int PositionChangeRank = 10;

    private sealed record PositionChangeRow(
        int Sequence,
        PositionChangeKind Kind,
        PositionChangeCause Cause,
        DateTimeOffset OccurredAt,
        PositionTrackingState TrackingStateAfter,
        decimal? BeforeSize,
        decimal? BeforeAverageEntryPrice,
        decimal? BeforePositionValue,
        decimal? BeforeLeverage,
        decimal? BeforeMarkPrice,
        decimal? BeforeBreakEvenPrice,
        decimal? BeforeLiquidationPrice,
        decimal? BeforeUnrealizedPnl,
        decimal? BeforeTakeProfit,
        decimal? BeforeStopLoss,
        decimal? BeforeTrailingStop,
        decimal AfterSize,
        decimal? AfterAverageEntryPrice,
        decimal? AfterPositionValue,
        decimal? AfterLeverage,
        decimal? AfterMarkPrice,
        decimal? AfterBreakEvenPrice,
        decimal? AfterLiquidationPrice,
        decimal? AfterUnrealizedPnl,
        decimal? AfterTakeProfit,
        decimal? AfterStopLoss,
        decimal? AfterTrailingStop);

    private sealed record EvaluationRow(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset ValidUntil,
        string RuleVersion,
        string? ResultJson,
        RiskIncreaseDecision PortfolioRiskDecision);

    private sealed record RecommendationRow(
        Guid Id,
        Guid AssessmentId,
        DateTimeOffset CreatedAt,
        DateTimeOffset ValidUntil,
        RecommendationStatus Status,
        PositionAction Action,
        decimal? Confidence,
        RecommendationPriority? Priority,
        AddDecision AddDecision,
        string? PolicyHash,
        string? DecisionContextJson);

    private sealed record ReasonRow(Guid SourceId, ReasonCode ReasonCode);
}
