using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Infrastructure.Persistence.Entities;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class RecommendationStabilityStateMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    public static RecommendationStabilityStateEntity ToEntity(
        PositionId positionId,
        RecommendationStabilityStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var state = snapshot.State;
        return new()
        {
            PositionId = positionId.Value,
            BaselineRecommendationId = snapshot.BaselineRecommendationId.Value,
            SemanticStateJson = JsonSerializer.Serialize(
                RecommendationStabilitySemanticStateDocument.FromDomain(state.SemanticState),
                JsonOptions),
            FirstObservedAt = PersistenceDateTime.ToUtc(state.FirstObservedAt),
            LastObservedAt = PersistenceDateTime.ToUtc(state.LastObservedAt),
            ConsecutiveObservations = state.ConsecutiveObservations
        };
    }

    public static RecommendationStabilityStateSnapshot ToDomain(
        RecommendationStabilityStateEntity entity)
    {
        if (entity.PositionId == Guid.Empty)
            throw new InvalidOperationException("Recommendation stability state position id is empty.");
        if (entity.BaselineRecommendationId == Guid.Empty)
            throw new InvalidOperationException(
                "Recommendation stability state baseline recommendation id is empty.");
        if (entity.Version <= 0)
            throw new InvalidOperationException(
                $"Recommendation stability state {entity.PositionId} has an invalid version.");
        if (string.IsNullOrWhiteSpace(entity.SemanticStateJson))
            throw new InvalidOperationException(
                $"Recommendation stability state {entity.PositionId} contains an empty semantic state.");

        RecommendationStabilitySemanticStateDocument document;
        try
        {
            document = JsonSerializer.Deserialize<RecommendationStabilitySemanticStateDocument>(
                entity.SemanticStateJson,
                JsonOptions) ?? throw new InvalidOperationException(
                    $"Recommendation stability state {entity.PositionId} contains an empty semantic state.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Recommendation stability state {entity.PositionId} contains malformed semantic state.",
                exception);
        }

        var semanticState = document.ToDomain(entity.PositionId);
        var firstObservedAt = PersistenceDateTime.ToUtc(entity.FirstObservedAt);
        var lastObservedAt = PersistenceDateTime.ToUtc(entity.LastObservedAt);
        if (firstObservedAt == default || lastObservedAt == default)
            throw new InvalidOperationException(
                $"Recommendation stability state {entity.PositionId} contains an uninitialized observation timestamp.");

        var state = new RecommendationStabilityState(
            semanticState,
            firstObservedAt,
            lastObservedAt,
            entity.ConsecutiveObservations);
        return new(
            RecommendationId.FromGuid(entity.BaselineRecommendationId),
            state);
    }

    private sealed record RecommendationStabilitySemanticStateDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; init; }
        public PositionAction? PositionAction { get; init; }
        public AddDecision? AddDecision { get; init; }
        public RecommendationPriority? Priority { get; init; }
        public IReadOnlyList<ReasonCode>? ActionReasonCodes { get; init; }
        public IReadOnlyList<ReasonCode>? AddReasonCodes { get; init; }
        public IReadOnlyList<ReasonCode>? InheritedReasonCodes { get; init; }
        public string? PolicyVersion { get; init; }
        public string? PolicyHash { get; init; }
        public decimal? MaximumAdditionalPositionValue { get; init; }
        public decimal? MaximumAdditionalQuantity { get; init; }

        public static RecommendationStabilitySemanticStateDocument FromDomain(
            RecommendationSemanticState state) =>
            new()
            {
                SchemaVersion = CurrentSchemaVersion,
                PositionAction = state.PositionAction,
                AddDecision = state.AddDecision,
                Priority = state.Priority,
                ActionReasonCodes = state.ActionReasonCodes,
                AddReasonCodes = state.AddReasonCodes,
                InheritedReasonCodes = state.InheritedReasonCodes,
                PolicyVersion = state.PolicyIdentity.Version,
                PolicyHash = state.PolicyIdentity.Hash,
                MaximumAdditionalPositionValue = state.MaximumAdditionalPositionValue,
                MaximumAdditionalQuantity = state.MaximumAdditionalQuantity
            };

        public RecommendationSemanticState ToDomain(Guid positionId)
        {
            if (SchemaVersion != CurrentSchemaVersion)
                throw new InvalidOperationException(
                    $"Recommendation stability state {positionId} uses unsupported semantic schema " +
                    $"{SchemaVersion}.");
            if (PositionAction is not { } positionAction ||
                AddDecision is not { } addDecision ||
                Priority is not { } priority ||
                ActionReasonCodes is null ||
                AddReasonCodes is null ||
                InheritedReasonCodes is null ||
                string.IsNullOrWhiteSpace(PolicyVersion) ||
                string.IsNullOrWhiteSpace(PolicyHash))
                throw new InvalidOperationException(
                    $"Recommendation stability state {positionId} contains incomplete semantic state.");

            ValidateReasons(ActionReasonCodes, nameof(ActionReasonCodes), positionId);
            ValidateReasons(AddReasonCodes, nameof(AddReasonCodes), positionId);
            ValidateReasons(InheritedReasonCodes, nameof(InheritedReasonCodes), positionId);

            return new(
                positionAction,
                addDecision,
                priority,
                Normalize(ActionReasonCodes),
                Normalize(AddReasonCodes),
                PolicyConfigurationIdentity.From(PolicyVersion, PolicyHash),
                MaximumAdditionalPositionValue,
                MaximumAdditionalQuantity,
                Normalize(InheritedReasonCodes));
        }

        private static void ValidateReasons(
            IEnumerable<ReasonCode> reasons,
            string fieldName,
            Guid positionId)
        {
            if (reasons.Any(reason => !Enum.IsDefined(reason)))
                throw new InvalidOperationException(
                    $"Recommendation stability state {positionId} contains an invalid {fieldName}.");
        }

        private static ReasonCode[] Normalize(IEnumerable<ReasonCode> reasons) =>
            reasons
                .Distinct()
                .OrderBy(reason => (int)reason)
                .ToArray();
    }
}
