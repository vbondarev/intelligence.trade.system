using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class PositionAssessmentMapper
{
    public static PositionAssessmentEntity ToEntity(PositionAssessment assessment) => new()
    {
        Id = assessment.Id.Value,
        PositionId = assessment.InputVersions.PositionId.Value,
        ExchangeAccountId = assessment.InputVersions.ExchangeAccountId.Value,
        InstrumentId = assessment.InputVersions.InstrumentId.Value,
        PositionObservedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.PositionObservedAt),
        PortfolioCalculatedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.PortfolioCalculatedAt),
        MarketCapturedAt = PersistenceDateTime.ToUtc(assessment.InputVersions.MarketCapturedAt),
        RuleVersion = assessment.RuleVersion.Value,
        CreatedAt = PersistenceDateTime.ToUtc(assessment.CreatedAt),
        ValidUntil = PersistenceDateTime.ToUtc(assessment.ValidUntil),
        PortfolioRiskDecision = assessment.PortfolioRiskDecision,
    };

    public static IReadOnlyList<PositionAssessmentReasonEntity> ToReasonEntities(
        PositionAssessment assessment) =>
        assessment.ReasonCodes
            .Select((reason, index) => new PositionAssessmentReasonEntity
            {
                PositionAssessmentId = assessment.Id.Value,
                Sequence = index + 1,
                ReasonCode = reason,
            })
            .ToArray();

    public static PositionAssessment ToDomain(
        PositionAssessmentEntity entity,
        IReadOnlyCollection<PositionAssessmentReasonEntity> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);

        return PositionAssessment.Restore(
            PositionAssessmentId.FromGuid(entity.Id),
            new PositionAssessmentInputVersions(
                PositionId.FromGuid(entity.PositionId),
                ExchangeAccountId.FromGuid(entity.ExchangeAccountId),
                InstrumentId.From(entity.InstrumentId),
                entity.PositionObservedAt,
                entity.PortfolioCalculatedAt,
                entity.MarketCapturedAt),
            RuleVersion.From(entity.RuleVersion),
            entity.CreatedAt,
            entity.ValidUntil,
            entity.PortfolioRiskDecision,
            reasons.OrderBy(reason => reason.Sequence).Select(reason => reason.ReasonCode));
    }
}
