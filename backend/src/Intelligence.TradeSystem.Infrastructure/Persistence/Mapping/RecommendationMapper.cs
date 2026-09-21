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
            NextEvaluationAt = recommendation.HasContinuationPlan
                ? PersistenceDateTime.ToUtc(recommendation.NextEvaluationAt)
                : null,
            ContinuationContextJson = recommendation.HasContinuationPlan
                ? RecommendationContinuationPersistenceMapper.Serialize(recommendation.ContinuationPlan!)
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

    public static bool DecisionContextsEqual(string? left, string? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        using var leftDocument = JsonDocument.Parse(left);
        using var rightDocument = JsonDocument.Parse(right);
        return JsonElementsEqual(leftDocument.RootElement, rightDocument.RootElement);
    }

    public static bool ContinuationContextsEqual(string? left, string? right) =>
        DecisionContextsEqual(left, right);

    public static bool IsLegacy(
        string? policyHash,
        decimal? confidence,
        RecommendationPriority? priority,
        string? decisionContextJson)
    {
        var any = policyHash is not null ||
            confidence is not null ||
            priority is not null ||
            decisionContextJson is not null;
        var complete = policyHash is not null &&
            confidence is not null &&
            priority is not null &&
            decisionContextJson is not null;
        if (any && !complete)
            throw new InvalidOperationException(
                "Recommendation contains incomplete structured decision metadata.");
        return !complete;
    }

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

        var hasContinuationMetadata = HasAnyContinuationMetadata(entity);
        if (!HasCompleteStructuredMetadata(entity))
        {
            if (hasContinuationMetadata)
                throw new InvalidOperationException(
                    $"Recommendation {entity.Id} contains continuation metadata without structured decision metadata.");
            return Recommendation.RestoreLegacy(
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

        var createdAt = PersistenceDateTime.ToUtc(entity.CreatedAt);
        var validUntil = PersistenceDateTime.ToUtc(entity.ValidUntil);
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
        RecommendationContinuationPlan? continuationPlan = null;
        if (hasContinuationMetadata)
        {
            if (entity.ContinuationContextJson is null || entity.NextEvaluationAt is null)
                throw new InvalidOperationException(
                    $"Recommendation {entity.Id} contains incomplete continuation metadata.");
            continuationPlan = RecommendationContinuationPersistenceMapper.Deserialize(
                entity.ContinuationContextJson,
                entity.Id,
                createdAt,
                validUntil,
                PersistenceDateTime.ToUtc(entity.NextEvaluationAt));
        }

        return Recommendation.RestoreStructured(
            RecommendationId.FromGuid(entity.Id),
            assessment,
            action,
            addDecision,
            PolicyConfigurationIdentity.From(entity.PolicyVersion, entity.PolicyHash!),
            orderedReasons,
            createdAt,
            validUntil,
            entity.Status,
            PersistenceDateTime.ToUtc(entity.AcknowledgedAt),
            PersistenceDateTime.ToUtc(entity.DismissedAt),
            PersistenceDateTime.ToUtc(entity.SupersededAt),
            PersistenceDateTime.ToUtc(entity.ExpiredAt),
            entity.SupersededByRecommendationId is { } successor
                ? RecommendationId.FromGuid(successor)
                : null,
            continuationPlan);
    }

    private static bool HasCompleteStructuredMetadata(RecommendationEntity entity)
    {
        try
        {
            return !IsLegacy(
                entity.PolicyHash,
                entity.Confidence,
                entity.Priority,
                entity.DecisionContextJson);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Recommendation {entity.Id} contains incomplete structured decision metadata.",
                exception);
        }
    }

    private static bool HasAnyContinuationMetadata(RecommendationEntity entity) =>
        entity.NextEvaluationAt is not null || entity.ContinuationContextJson is not null;

    private static bool JsonElementsEqual(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectsEqual(left, right),
            JsonValueKind.Array => left.EnumerateArray().Zip(
                    right.EnumerateArray(),
                    JsonElementsEqual)
                .All(equal => equal) &&
                left.GetArrayLength() == right.GetArrayLength(),
            JsonValueKind.Number => NumbersEqual(left, right),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.True or JsonValueKind.False =>
                left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            _ => false
        };
    }

    private static bool ObjectsEqual(JsonElement left, JsonElement right)
    {
        var rightProperties = right.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        foreach (var property in left.EnumerateObject())
        {
            if (!rightProperties.TryGetValue(property.Name, out var rightValue) ||
                !JsonElementsEqual(property.Value, rightValue))
                return false;
        }

        return left.EnumerateObject().Count() == rightProperties.Count;
    }

    private static bool NumbersEqual(JsonElement left, JsonElement right) =>
        left.TryGetDecimal(out var leftValue) &&
        right.TryGetDecimal(out var rightValue) &&
        leftValue == rightValue;
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
