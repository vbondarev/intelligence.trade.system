using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class RecommendationMapper
{
    public static RecommendationEntity ToEntity(Recommendation recommendation) => new()
    {
        Id = recommendation.Id.Value,
        AssessmentId = recommendation.AssessmentId.Value,
        PositionId = recommendation.PositionId.Value,
        RecommendedAction = recommendation.RecommendedAction,
        AddDecision = recommendation.AddDecision,
        PolicyVersion = recommendation.PolicyVersion.Value,
        CreatedAt = PersistenceDateTime.ToUtc(recommendation.CreatedAt),
        ValidUntil = PersistenceDateTime.ToUtc(recommendation.ValidUntil),
        Status = recommendation.Status,
        AcknowledgedAt = PersistenceDateTime.ToUtc(recommendation.AcknowledgedAt),
        DismissedAt = PersistenceDateTime.ToUtc(recommendation.DismissedAt),
        SupersededAt = PersistenceDateTime.ToUtc(recommendation.SupersededAt),
        ExpiredAt = PersistenceDateTime.ToUtc(recommendation.ExpiredAt),
        SupersededByRecommendationId = recommendation.SupersededByRecommendationId?.Value,
    };

    public static IReadOnlyList<RecommendationReasonEntity> ToReasonEntities(
        Recommendation recommendation) =>
        recommendation.ReasonCodes
            .Select((reason, index) => new RecommendationReasonEntity
            {
                RecommendationId = recommendation.Id.Value,
                Sequence = index + 1,
                ReasonCode = reason,
            })
            .ToArray();

    public static Recommendation ToDomain(
        RecommendationEntity entity,
        IReadOnlyCollection<RecommendationReasonEntity> reasons,
        PositionAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        ArgumentNullException.ThrowIfNull(assessment);
        if (entity.AssessmentId != assessment.Id.Value)
            throw new InvalidOperationException(
                $"Recommendation {entity.Id} references a different position assessment.");
        if (entity.PositionId != assessment.PositionId.Value)
            throw new InvalidOperationException(
                $"Recommendation {entity.Id} references a different position.");

        return Recommendation.Restore(
            RecommendationId.FromGuid(entity.Id),
            assessment,
            entity.RecommendedAction,
            entity.AddDecision,
            RuleVersion.From(entity.PolicyVersion),
            reasons.OrderBy(reason => reason.Sequence).Select(reason => reason.ReasonCode),
            PersistenceDateTime.ToUtc(entity.CreatedAt),
            PersistenceDateTime.ToUtc(entity.ValidUntil),
            entity.Status,
            PersistenceDateTime.ToUtc(entity.AcknowledgedAt),
            PersistenceDateTime.ToUtc(entity.DismissedAt),
            PersistenceDateTime.ToUtc(entity.SupersededAt),
            PersistenceDateTime.ToUtc(entity.ExpiredAt),
            entity.SupersededByRecommendationId is { } successor
                ? RecommendationId.FromGuid(successor)
                : null);
    }
}
