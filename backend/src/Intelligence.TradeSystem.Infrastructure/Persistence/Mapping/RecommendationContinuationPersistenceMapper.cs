using System.Text.Json;
using System.Text.Json.Serialization;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Infrastructure.Persistence.Mapping;

internal static class RecommendationContinuationPersistenceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Serialize(RecommendationContinuationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(ContinuationDocument.FromDomain(plan), JsonOptions);
    }

    public static RecommendationContinuationPlan Deserialize(
        string json,
        Guid recommendationId,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil,
        DateTimeOffset? nextEvaluationAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var document = JsonSerializer.Deserialize<ContinuationDocument>(json, JsonOptions)
            ?? throw Invalid(recommendationId, "Continuation context is empty.");
        if (document.SchemaVersion != ContinuationDocument.CurrentSchemaVersion)
            throw Invalid(recommendationId, $"Unsupported continuation schema {document.SchemaVersion}.");
        if (document.CreatedAt != createdAt || document.ValidUntil != validUntil)
            throw Invalid(recommendationId, "Continuation timestamps do not match recommendation timestamps.");
        if (nextEvaluationAt is null)
            throw Invalid(recommendationId, "Continuation context requires NextEvaluationAt.");
        if (document.InvalidationConditions is null || document.ReevaluationConditions is null)
            throw Invalid(recommendationId, "Continuation condition lists are required.");

        try
        {
            return new RecommendationContinuationPlan(
                document.InvalidationConditions.Select(ConditionDocument.ToDomain),
                document.ReevaluationConditions.Select(ConditionDocument.ToDomain),
                createdAt,
                validUntil,
                nextEvaluationAt.Value);
        }
        catch (ArgumentException exception)
        {
            throw Invalid(recommendationId, "Continuation context contains invalid typed conditions.", exception);
        }
    }

    private static InvalidOperationException Invalid(
        Guid recommendationId,
        string message,
        Exception? innerException = null) =>
        new($"Recommendation {recommendationId}: {message}", innerException);

    private sealed class ContinuationDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
        public DateTimeOffset? ValidUntil { get; init; }
        public IReadOnlyList<ConditionDocument>? InvalidationConditions { get; init; }
        public IReadOnlyList<ConditionDocument>? ReevaluationConditions { get; init; }

        public static ContinuationDocument FromDomain(RecommendationContinuationPlan plan) => new()
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedAt = plan.CreatedAt,
            ValidUntil = plan.ValidUntil,
            InvalidationConditions = plan.InvalidationConditions.Select(ConditionDocument.FromDomain).ToArray(),
            ReevaluationConditions = plan.ReevaluationConditions.Select(ConditionDocument.FromDomain).ToArray()
        };
    }

    private sealed class ConditionDocument
    {
        public RecommendationContinuationConditionScope? Scope { get; init; }
        public RecommendationContinuationConditionKind? Kind { get; init; }
        public PositionTrendAlignment? RequiredAlignment { get; init; }
        public bool? RequiredReliability { get; init; }
        public AssessmentMomentumState? RequiredMomentumState { get; init; }
        public bool? RequiredAvailability { get; init; }
        public bool? RequiredExhaustion { get; init; }
        public bool? RequiredProtective { get; init; }
        public AssessmentLiquidationState? RequiredLiquidationState { get; init; }
        public decimal? MinimumDistancePercent { get; init; }
        public RecommendationPnlComparison? Comparison { get; init; }
        public decimal? Threshold { get; init; }
        public AssessmentDataQuality? RequiredDataQuality { get; init; }
        public AssessmentSafetyState? RequiredSafetyState { get; init; }
        public RiskIncreaseDecision? RequiredRiskDecision { get; init; }
        public bool? RequiredLowVolume { get; init; }
        public string? RequiredPolicyVersion { get; init; }
        public string? RequiredPolicyHash { get; init; }
        public decimal? MaximumPositionValue { get; init; }
        public decimal? MaximumQuantity { get; init; }
        public RecommendationOpposingLevel? RequiredLevel { get; init; }
        public DateTimeOffset? ValidUntil { get; init; }

        public static ConditionDocument FromDomain(RecommendationContinuationCondition condition) => condition switch
        {
            TrendAlignmentCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredAlignment = value.RequiredAlignment
            },
            MomentumReliabilityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredReliability = value.RequiredReliability
            },
            MomentumStateCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredMomentumState = value.RequiredState
            },
            MomentumAvailabilityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredAvailability = value.RequiredAvailability
            },
            MomentumExhaustionCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredExhaustion = value.RequiredExhaustion
            },
            StopProtectionCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredProtective = value.RequiredProtective
            },
            StopAvailabilityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredAvailability = value.RequiredAvailability
            },
            LiquidationStateCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredLiquidationState = value.RequiredState
            },
            LiquidationDistanceCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                MinimumDistancePercent = value.MinimumDistancePercent
            },
            PnlThresholdCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                Comparison = value.Comparison,
                Threshold = value.Threshold
            },
            DataQualityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredDataQuality = value.RequiredQuality
            },
            SafetyStateCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredSafetyState = value.RequiredState
            },
            PortfolioRiskDecisionCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredRiskDecision = value.RequiredDecision
            },
            LowVolumeCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredLowVolume = value.RequiredLowVolume
            },
            PolicyIdentityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredPolicyVersion = value.RequiredIdentity.Version,
                RequiredPolicyHash = value.RequiredIdentity.Hash
            },
            AddAllowedCapacityCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                MaximumPositionValue = value.MaximumPositionValue,
                MaximumQuantity = value.MaximumQuantity
            },
            OpposingLevelCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                RequiredLevel = value.RequiredLevel
            },
            RecommendationExpiryCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind,
                ValidUntil = value.ValidUntil
            },
            ContinuationContextUnavailableCondition value => new()
            {
                Scope = value.Scope,
                Kind = value.Kind
            },
            _ => throw new InvalidOperationException(
                $"Unsupported continuation condition type '{condition.GetType().Name}'.")
        };

        public static RecommendationContinuationCondition ToDomain(ConditionDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            var kind = Required(document.Kind, nameof(Kind));
            var scope = Required(document.Scope, nameof(Scope));
            return kind switch
            {
                RecommendationContinuationConditionKind.TrendAlignment =>
                    new TrendAlignmentCondition(scope, Required(document.RequiredAlignment, nameof(RequiredAlignment))),
                RecommendationContinuationConditionKind.MomentumReliability =>
                    new MomentumReliabilityCondition(scope, Required(document.RequiredReliability, nameof(RequiredReliability))),
                RecommendationContinuationConditionKind.MomentumState =>
                    new MomentumStateCondition(scope, Required(document.RequiredMomentumState, nameof(RequiredMomentumState))),
                RecommendationContinuationConditionKind.MomentumAvailability =>
                    new MomentumAvailabilityCondition(scope, Required(document.RequiredAvailability, nameof(RequiredAvailability))),
                RecommendationContinuationConditionKind.MomentumExhaustion =>
                    new MomentumExhaustionCondition(scope, Required(document.RequiredExhaustion, nameof(RequiredExhaustion))),
                RecommendationContinuationConditionKind.StopProtection =>
                    new StopProtectionCondition(scope, Required(document.RequiredProtective, nameof(RequiredProtective))),
                RecommendationContinuationConditionKind.StopAvailability =>
                    new StopAvailabilityCondition(scope, Required(document.RequiredAvailability, nameof(RequiredAvailability))),
                RecommendationContinuationConditionKind.LiquidationState =>
                    new LiquidationStateCondition(scope, Required(document.RequiredLiquidationState, nameof(RequiredLiquidationState))),
                RecommendationContinuationConditionKind.LiquidationDistance =>
                    new LiquidationDistanceCondition(scope, Required(document.MinimumDistancePercent, nameof(MinimumDistancePercent))),
                RecommendationContinuationConditionKind.PnlThreshold =>
                    new PnlThresholdCondition(
                        scope,
                        Required(document.Comparison, nameof(Comparison)),
                        Required(document.Threshold, nameof(Threshold))),
                RecommendationContinuationConditionKind.DataQuality =>
                    new DataQualityCondition(scope, Required(document.RequiredDataQuality, nameof(RequiredDataQuality))),
                RecommendationContinuationConditionKind.SafetyState =>
                    new SafetyStateCondition(scope, Required(document.RequiredSafetyState, nameof(RequiredSafetyState))),
                RecommendationContinuationConditionKind.PortfolioRiskDecision =>
                    new PortfolioRiskDecisionCondition(scope, Required(document.RequiredRiskDecision, nameof(RequiredRiskDecision))),
                RecommendationContinuationConditionKind.LowVolume =>
                    new LowVolumeCondition(scope, Required(document.RequiredLowVolume, nameof(RequiredLowVolume))),
                RecommendationContinuationConditionKind.PolicyIdentity =>
                    new PolicyIdentityCondition(
                        scope,
                        PolicyConfigurationIdentity.From(
                            Required(document.RequiredPolicyVersion, nameof(RequiredPolicyVersion)),
                            Required(document.RequiredPolicyHash, nameof(RequiredPolicyHash)))),
                RecommendationContinuationConditionKind.AddAllowedCapacity =>
                    CreateCapacity(document, scope),
                RecommendationContinuationConditionKind.OpposingLevel =>
                    new OpposingLevelCondition(scope, Required(document.RequiredLevel, nameof(RequiredLevel))),
                RecommendationContinuationConditionKind.RecommendationExpiry =>
                    new RecommendationExpiryCondition(Required(document.ValidUntil, nameof(ValidUntil))),
                RecommendationContinuationConditionKind.ContinuationContextUnavailable =>
                    new ContinuationContextUnavailableCondition(),
                _ => throw new ArgumentOutOfRangeException(nameof(document), kind, "Condition kind must be defined.")
            };
        }

        private static AddAllowedCapacityCondition CreateCapacity(
            ConditionDocument document,
            RecommendationContinuationConditionScope scope)
        {
            if (scope != RecommendationContinuationConditionScope.AddDecision)
                throw new ArgumentException("AddAllowed capacity must use AddDecision scope.", nameof(scope));
            return new(
                Required(document.MaximumPositionValue, nameof(MaximumPositionValue)),
                document.MaximumQuantity);
        }

        private static T Required<T>(T? value, string name) where T : struct =>
            value ?? throw new ArgumentException($"Continuation condition property '{name}' is required.", name);

        private static string Required(string? value, string name) =>
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Continuation condition property '{name}' is required.", name);
    }
}


