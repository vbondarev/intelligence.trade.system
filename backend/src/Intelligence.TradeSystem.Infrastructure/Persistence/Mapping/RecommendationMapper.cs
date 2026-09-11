using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class RecommendationMapper
{
    private static readonly JsonSerializerOptions DecisionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static RecommendationEntity ToEntity(Recommendation recommendation)
    {
        var structured = recommendation.HasStructuredDecision;
        return new()
        {
            Id = recommendation.Id.Value,
            AssessmentId = recommendation.AssessmentId.Value,
            PositionId = recommendation.PositionId.Value,
            RecommendedAction = recommendation.RecommendedAction,
            AddDecision = recommendation.AddDecision,
            PolicyVersion = recommendation.PolicyVersion.Value,
            PolicyHash = structured ? recommendation.PolicyHash : null,
            Confidence = structured ? recommendation.Confidence : null,
            Priority = structured ? recommendation.Priority : null,
            DecisionContextJson = structured
                ? JsonSerializer.Serialize(
                    new RecommendationDecisionDocument(
                        RecommendationDecisionDocument.CurrentSchemaVersion,
                        recommendation.ActionReasonCodes,
                        recommendation.AddReasonCodes,
                        recommendation.MaximumAdditionalPositionValue,
                        recommendation.MaximumAdditionalQuantity,
                        recommendation.AddConditions),
                    DecisionJsonOptions)
                : null,
            CreatedAt = PersistenceDateTime.ToUtc(recommendation.CreatedAt),
            ValidUntil = PersistenceDateTime.ToUtc(recommendation.ValidUntil),
            Status = recommendation.Status,
            AcknowledgedAt = PersistenceDateTime.ToUtc(recommendation.AcknowledgedAt),
            DismissedAt = PersistenceDateTime.ToUtc(recommendation.DismissedAt),
            SupersededAt = PersistenceDateTime.ToUtc(recommendation.SupersededAt),
            ExpiredAt = PersistenceDateTime.ToUtc(recommendation.ExpiredAt),
            SupersededByRecommendationId = recommendation.SupersededByRecommendationId?.Value,
        };
    }

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

        var orderedReasons = reasons
            .OrderBy(reason => reason.Sequence)
            .Select(reason => reason.ReasonCode)
            .ToArray();

        if (!HasCompleteStructuredMetadata(entity))
        {
            return Recommendation.Restore(
                RecommendationId.FromGuid(entity.Id),
                assessment,
                entity.RecommendedAction,
                entity.AddDecision,
                RuleVersion.From(entity.PolicyVersion),
                orderedReasons,
                PersistenceDateTime.ToUtc(entity.CreatedAt),
                PersistenceDateTime.ToUtc(entity.ValidUntil),
                entity.Status,
                PersistenceDateTime.ToUtc(entity.AcknowledgedAt),
                PersistenceDateTime.ToUtc(entity.DismissedAt),
                PersistenceDateTime.ToUtc(entity.SupersededAt),
                PersistenceDateTime.ToUtc(entity.ExpiredAt),
                entity.SupersededByRecommendationId is { } successorId
                    ? RecommendationId.FromGuid(successorId)
                    : null);
        }

        var document = JsonSerializer.Deserialize<RecommendationDecisionDocument>(
            entity.DecisionContextJson!,
            DecisionJsonOptions)
            ?? throw new InvalidOperationException(
                $"Recommendation {entity.Id} contains an empty decision context.");
        if (document.SchemaVersion != RecommendationDecisionDocument.CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Recommendation {entity.Id} uses unsupported decision schema {document.SchemaVersion}.");

        var action = new RecommendedActionDecision(
            entity.RecommendedAction,
            entity.Confidence!.Value,
            entity.Priority!.Value,
            document.ActionReasonCodes);
        var addDecision = new AddDecisionResult(
            entity.AddDecision,
            document.AddReasonCodes,
            document.MaximumAdditionalPositionValue,
            document.MaximumAdditionalQuantity,
            document.Conditions);

        return Recommendation.Restore(
            RecommendationId.FromGuid(entity.Id),
            assessment,
            action,
            addDecision,
            PolicyConfigurationIdentity.From(entity.PolicyVersion, entity.PolicyHash!),
            orderedReasons,
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

    private static bool HasCompleteStructuredMetadata(RecommendationEntity entity)
    {
        var any = entity.PolicyHash is not null ||
            entity.Confidence is not null ||
            entity.Priority is not null ||
            entity.DecisionContextJson is not null;
        var complete = entity.PolicyHash is not null &&
            entity.Confidence is not null &&
            entity.Priority is not null &&
            entity.DecisionContextJson is not null;
        if (any && !complete)
            throw new InvalidOperationException(
                $"Recommendation {entity.Id} contains incomplete structured decision metadata.");
        return complete;
    }
}

internal sealed record RecommendationDecisionDocument(
    int SchemaVersion,
    IReadOnlyList<ReasonCode> ActionReasonCodes,
    IReadOnlyList<ReasonCode> AddReasonCodes,
    decimal? MaximumAdditionalPositionValue,
    decimal? MaximumAdditionalQuantity,
    AddDecisionConditions? Conditions)
{
    public const int CurrentSchemaVersion = 1;
}
