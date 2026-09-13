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
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Serialize(RecommendationContinuationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return JsonSerializer.Serialize(
            ContinuationDocument.FromDomain(plan),
            JsonOptions);
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
        using var rawDocument = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
        ValidateRawDocument(rawDocument.RootElement, document, recommendationId);
        if (document.SchemaVersion != ContinuationDocument.CurrentSchemaVersion)
            throw Invalid(recommendationId, $"Unsupported continuation schema {document.SchemaVersion}.");

        var documentCreatedAt = Required(document.CreatedAt, nameof(document.CreatedAt));
        var documentValidUntil = Required(document.ValidUntil, nameof(document.ValidUntil));
        if (documentCreatedAt != createdAt || documentValidUntil != validUntil)
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
                PersistenceDateTime.ToUtc(nextEvaluationAt.Value));
        }
        catch (ArgumentException exception)
        {
            throw Invalid(recommendationId, "Continuation context contains invalid typed conditions.", exception);
        }
    }

    private static T Required<T>(T? value, string name) where T : struct =>
        value ?? throw new ArgumentException($"Continuation property '{name}' is required.", name);

    private static InvalidOperationException Invalid(
        Guid recommendationId,
        string message,
        Exception? innerException = null) =>
        new($"Recommendation {recommendationId}: {message}", innerException);

    private static void ValidateRawDocument(
        JsonElement root,
        ContinuationDocument document,
        Guid recommendationId)
    {
        var rootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!rootNames.Add(property.Name))
                throw Invalid(recommendationId, $"Duplicate continuation property '{property.Name}'.");
        }

        ValidateRawConditionArray(
            root,
            "invalidationConditions",
            document.InvalidationConditions,
            recommendationId);
        ValidateRawConditionArray(
            root,
            "reevaluationConditions",
            document.ReevaluationConditions,
            recommendationId);
    }

    private static void ValidateRawConditionArray(
        JsonElement root,
        string propertyName,
        IReadOnlyList<ConditionDocument>? documents,
        Guid recommendationId)
    {
        if (!TryGetProperty(root, propertyName, out var array))
            return;
        if (array.ValueKind != JsonValueKind.Array || documents is null)
            return;

        var rawConditions = array.EnumerateArray().ToArray();
        if (rawConditions.Length != documents.Count)
            throw Invalid(recommendationId, $"Continuation property '{propertyName}' is malformed.");

        for (var index = 0; index < rawConditions.Length; index++)
        {
            if (rawConditions[index].ValueKind != JsonValueKind.Object)
                throw Invalid(recommendationId, "Continuation condition entries must be objects.");
            var condition = documents[index];
            var kind = Required(condition.Kind, nameof(ConditionDocument.Kind));
            var allowed = AllowedPayloadProperties(kind);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in rawConditions[index].EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw Invalid(recommendationId, $"Duplicate condition property '{property.Name}'.");
                if (!allowed.Contains(property.Name))
                    throw Invalid(
                        recommendationId,
                        $"Payload property '{property.Name}' is not valid for condition kind '{kind}'.");
            }
        }
    }

    private static HashSet<string> AllowedPayloadProperties(
        RecommendationContinuationConditionKind kind)
    {
        var allowed = new HashSet<string>(
            ["scope", "kind"],
            StringComparer.OrdinalIgnoreCase);
        allowed.UnionWith(kind switch
        {
            RecommendationContinuationConditionKind.TrendAlignment =>
                ["requiredAlignment"],
            RecommendationContinuationConditionKind.MomentumReliability =>
                ["requiredReliability"],
            RecommendationContinuationConditionKind.MomentumState =>
                ["requiredMomentumState"],
            RecommendationContinuationConditionKind.MomentumAvailability =>
                ["requiredAvailability"],
            RecommendationContinuationConditionKind.MomentumExhaustion =>
                ["requiredExhaustion"],
            RecommendationContinuationConditionKind.StopState =>
                ["requiredStopState"],
            RecommendationContinuationConditionKind.StopAvailability =>
                ["requiredAvailability"],
            RecommendationContinuationConditionKind.StopRelativePosition =>
                ["requiredStopRelativePosition"],
            RecommendationContinuationConditionKind.ProfitProtection =>
                ["requiredProfitProtection"],
            RecommendationContinuationConditionKind.LiquidationState =>
                ["requiredLiquidationState"],
            RecommendationContinuationConditionKind.LiquidationDistance =>
                ["minimumDistancePercent"],
            RecommendationContinuationConditionKind.PnlThreshold =>
                ["comparison", "threshold"],
            RecommendationContinuationConditionKind.PnlAvailability =>
                ["requiredAvailability"],
            RecommendationContinuationConditionKind.DataQuality =>
                ["requiredDataQuality"],
            RecommendationContinuationConditionKind.SafetyState =>
                ["requiredSafetyState"],
            RecommendationContinuationConditionKind.PortfolioRiskDecision =>
                ["requiredRiskDecision"],
            RecommendationContinuationConditionKind.LowVolume =>
                ["requiredLowVolume"],
            RecommendationContinuationConditionKind.PolicyIdentity =>
                ["requiredPolicyVersion", "requiredPolicyHash"],
            RecommendationContinuationConditionKind.AddAllowedCapacity =>
                ["maximumPositionValue", "maximumQuantity"],
            RecommendationContinuationConditionKind.OpposingLevel =>
                ["requiredLevel"],
            RecommendationContinuationConditionKind.RecommendationExpiry =>
                ["validUntil"],
            RecommendationContinuationConditionKind.ContinuationContextUnavailable =>
                [],
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Condition kind must be defined.")
        });
        return allowed;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed class ContinuationDocument
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion { get; init; }
        public DateTimeOffset? CreatedAt { get; init; }
        public DateTimeOffset? ValidUntil { get; init; }
        public IReadOnlyList<ConditionDocument>? InvalidationConditions { get; init; }
        public IReadOnlyList<ConditionDocument>? ReevaluationConditions { get; init; }

        public static ContinuationDocument FromDomain(RecommendationContinuationPlan plan) => new()
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedAt = PersistenceDateTime.ToUtc(plan.CreatedAt),
            ValidUntil = PersistenceDateTime.ToUtc(plan.ValidUntil),
            InvalidationConditions = plan.InvalidationConditions
                .Select(ConditionDocument.FromDomain)
                .ToArray(),
            ReevaluationConditions = plan.ReevaluationConditions
                .Select(ConditionDocument.FromDomain)
                .ToArray()
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
        public AssessmentStopState? RequiredStopState { get; init; }
        public AssessmentPricePosition? RequiredStopRelativePosition { get; init; }
        public bool? RequiredProfitProtection { get; init; }
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

        public static ConditionDocument FromDomain(
            RecommendationContinuationCondition condition) =>
            condition switch
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
                StopStateCondition value => new()
                {
                    Scope = value.Scope,
                    Kind = value.Kind,
                    RequiredStopState = value.RequiredState
                },
                StopAvailabilityCondition value => new()
                {
                    Scope = value.Scope,
                    Kind = value.Kind,
                    RequiredAvailability = value.RequiredAvailability
                },
                StopRelativePositionCondition value => new()
                {
                    Scope = value.Scope,
                    Kind = value.Kind,
                    RequiredStopRelativePosition = value.RequiredPosition
                },
                ProfitProtectionCondition value => new()
                {
                    Scope = value.Scope,
                    Kind = value.Kind,
                    RequiredProfitProtection = value.RequiredProtection
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
                PnlAvailabilityCondition value => new()
                {
                    Scope = value.Scope,
                    Kind = value.Kind,
                    RequiredAvailability = value.RequiredAvailability
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
                    ValidUntil = PersistenceDateTime.ToUtc(value.ValidUntil)
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
                    Create(
                        document,
                        nameof(RequiredAlignment),
                        new TrendAlignmentCondition(
                            scope,
                            Required(document.RequiredAlignment, nameof(RequiredAlignment)))),
                RecommendationContinuationConditionKind.MomentumReliability =>
                    Create(
                        document,
                        nameof(RequiredReliability),
                        new MomentumReliabilityCondition(
                            scope,
                            Required(document.RequiredReliability, nameof(RequiredReliability)))),
                RecommendationContinuationConditionKind.MomentumState =>
                    Create(
                        document,
                        nameof(RequiredMomentumState),
                        new MomentumStateCondition(
                            scope,
                            Required(document.RequiredMomentumState, nameof(RequiredMomentumState)))),
                RecommendationContinuationConditionKind.MomentumAvailability =>
                    Create(
                        document,
                        nameof(RequiredAvailability),
                        new MomentumAvailabilityCondition(
                            scope,
                            Required(document.RequiredAvailability, nameof(RequiredAvailability)))),
                RecommendationContinuationConditionKind.MomentumExhaustion =>
                    Create(
                        document,
                        nameof(RequiredExhaustion),
                        new MomentumExhaustionCondition(
                            scope,
                            Required(document.RequiredExhaustion, nameof(RequiredExhaustion)))),
                RecommendationContinuationConditionKind.StopState =>
                    Create(
                        document,
                        nameof(RequiredStopState),
                        new StopStateCondition(
                            scope,
                            Required(document.RequiredStopState, nameof(RequiredStopState)))),
                RecommendationContinuationConditionKind.StopAvailability =>
                    Create(
                        document,
                        nameof(RequiredAvailability),
                        new StopAvailabilityCondition(
                            scope,
                            Required(document.RequiredAvailability, nameof(RequiredAvailability)))),
                RecommendationContinuationConditionKind.StopRelativePosition =>
                    Create(
                        document,
                        nameof(RequiredStopRelativePosition),
                        new StopRelativePositionCondition(
                            scope,
                            Required(
                                document.RequiredStopRelativePosition,
                                nameof(RequiredStopRelativePosition)))),
                RecommendationContinuationConditionKind.ProfitProtection =>
                    Create(
                        document,
                        nameof(RequiredProfitProtection),
                        new ProfitProtectionCondition(
                            scope,
                            Required(document.RequiredProfitProtection, nameof(RequiredProfitProtection)))),
                RecommendationContinuationConditionKind.LiquidationState =>
                    Create(
                        document,
                        nameof(RequiredLiquidationState),
                        new LiquidationStateCondition(
                            scope,
                            Required(document.RequiredLiquidationState, nameof(RequiredLiquidationState)))),
                RecommendationContinuationConditionKind.LiquidationDistance =>
                    Create(
                        document,
                        nameof(MinimumDistancePercent),
                        new LiquidationDistanceCondition(
                            scope,
                            Required(document.MinimumDistancePercent, nameof(MinimumDistancePercent)))),
                RecommendationContinuationConditionKind.PnlThreshold =>
                    Create(
                        document,
                        nameof(Comparison),
                        nameof(Threshold),
                        new PnlThresholdCondition(
                            scope,
                            Required(document.Comparison, nameof(Comparison)),
                            Required(document.Threshold, nameof(Threshold)))),
                RecommendationContinuationConditionKind.PnlAvailability =>
                    Create(
                        document,
                        nameof(RequiredAvailability),
                        new PnlAvailabilityCondition(
                            scope,
                            Required(document.RequiredAvailability, nameof(RequiredAvailability)))),
                RecommendationContinuationConditionKind.DataQuality =>
                    Create(
                        document,
                        nameof(RequiredDataQuality),
                        new DataQualityCondition(
                            scope,
                            Required(document.RequiredDataQuality, nameof(RequiredDataQuality)))),
                RecommendationContinuationConditionKind.SafetyState =>
                    Create(
                        document,
                        nameof(RequiredSafetyState),
                        new SafetyStateCondition(
                            scope,
                            Required(document.RequiredSafetyState, nameof(RequiredSafetyState)))),
                RecommendationContinuationConditionKind.PortfolioRiskDecision =>
                    Create(
                        document,
                        nameof(RequiredRiskDecision),
                        new PortfolioRiskDecisionCondition(
                            scope,
                            Required(document.RequiredRiskDecision, nameof(RequiredRiskDecision)))),
                RecommendationContinuationConditionKind.LowVolume =>
                    Create(
                        document,
                        nameof(RequiredLowVolume),
                        new LowVolumeCondition(
                            scope,
                            Required(document.RequiredLowVolume, nameof(RequiredLowVolume)))),
                RecommendationContinuationConditionKind.PolicyIdentity =>
                    Create(
                        document,
                        nameof(RequiredPolicyVersion),
                        nameof(RequiredPolicyHash),
                        new PolicyIdentityCondition(
                            scope,
                            PolicyConfigurationIdentity.From(
                                Required(document.RequiredPolicyVersion, nameof(RequiredPolicyVersion)),
                                Required(document.RequiredPolicyHash, nameof(RequiredPolicyHash)))),
                        requireRecommendationScope: true),
                RecommendationContinuationConditionKind.AddAllowedCapacity =>
                    CreateCapacity(document, scope),
                RecommendationContinuationConditionKind.OpposingLevel =>
                    Create(
                        document,
                        nameof(RequiredLevel),
                        new OpposingLevelCondition(
                            scope,
                            Required(document.RequiredLevel, nameof(RequiredLevel)))),
                RecommendationContinuationConditionKind.RecommendationExpiry =>
                    Create(
                        document,
                        nameof(ValidUntil),
                        new RecommendationExpiryCondition(
                            PersistenceDateTime.ToUtc(
                                Required(document.ValidUntil, nameof(ValidUntil))))),
                RecommendationContinuationConditionKind.ContinuationContextUnavailable =>
                    Create(
                        document,
                        new ContinuationContextUnavailableCondition()),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(document),
                    kind,
                    "Condition kind must be defined.")
            };
        }

        private static RecommendationContinuationCondition Create(
            ConditionDocument document,
            RecommendationContinuationCondition condition,
            bool requireRecommendationScope = false)
        {
            document.ValidatePayload();
            if (requireRecommendationScope &&
                condition.Scope != RecommendationContinuationConditionScope.Recommendation)
                throw new ArgumentException(
                    "Policy identity conditions must use Recommendation scope.",
                    nameof(document));
            return condition;
        }

        private static RecommendationContinuationCondition Create(
            ConditionDocument document,
            string requiredProperty,
            RecommendationContinuationCondition condition,
            bool requireRecommendationScope = false) =>
            Create(document, [requiredProperty], condition, requireRecommendationScope);

        private static RecommendationContinuationCondition Create(
            ConditionDocument document,
            string firstRequiredProperty,
            string secondRequiredProperty,
            RecommendationContinuationCondition condition,
            bool requireRecommendationScope = false) =>
            Create(
                document,
                [firstRequiredProperty, secondRequiredProperty],
                condition,
                requireRecommendationScope);

        private static RecommendationContinuationCondition Create(
            ConditionDocument document,
            string[] requiredProperties,
            RecommendationContinuationCondition condition,
            bool requireRecommendationScope = false)
        {
            document.ValidatePayload(requiredProperties);
            if (requireRecommendationScope &&
                condition.Scope != RecommendationContinuationConditionScope.Recommendation)
                throw new ArgumentException(
                    "Policy identity conditions must use Recommendation scope.",
                    nameof(document));
            return condition;
        }

        private static AddAllowedCapacityCondition CreateCapacity(
            ConditionDocument document,
            RecommendationContinuationConditionScope scope)
        {
            if (scope != RecommendationContinuationConditionScope.AddDecision)
                throw new ArgumentException("AddAllowed capacity must use AddDecision scope.", nameof(scope));
            document.ValidatePayloadWithOptional(
                nameof(MaximumPositionValue),
                nameof(MaximumQuantity));
            return new(
                Required(document.MaximumPositionValue, nameof(MaximumPositionValue)),
                document.MaximumQuantity);
        }

        private void ValidatePayload(params string[] requiredProperties)
        {
            ValidatePayload(requiredProperties, []);
        }

        private void ValidatePayloadWithOptional(
            string requiredProperty,
            string optionalProperty) =>
            ValidatePayload([requiredProperty], [optionalProperty]);

        private void ValidatePayload(
            string[] requiredProperties,
            string[] optionalProperties)
        {
            var values = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [nameof(RequiredAlignment)] = RequiredAlignment is not null,
                [nameof(RequiredReliability)] = RequiredReliability is not null,
                [nameof(RequiredMomentumState)] = RequiredMomentumState is not null,
                [nameof(RequiredAvailability)] = RequiredAvailability is not null,
                [nameof(RequiredExhaustion)] = RequiredExhaustion is not null,
                [nameof(RequiredStopState)] = RequiredStopState is not null,
                [nameof(RequiredStopRelativePosition)] = RequiredStopRelativePosition is not null,
                [nameof(RequiredProfitProtection)] = RequiredProfitProtection is not null,
                [nameof(RequiredLiquidationState)] = RequiredLiquidationState is not null,
                [nameof(MinimumDistancePercent)] = MinimumDistancePercent is not null,
                [nameof(Comparison)] = Comparison is not null,
                [nameof(Threshold)] = Threshold is not null,
                [nameof(RequiredDataQuality)] = RequiredDataQuality is not null,
                [nameof(RequiredSafetyState)] = RequiredSafetyState is not null,
                [nameof(RequiredRiskDecision)] = RequiredRiskDecision is not null,
                [nameof(RequiredLowVolume)] = RequiredLowVolume is not null,
                [nameof(RequiredPolicyVersion)] = RequiredPolicyVersion is not null,
                [nameof(RequiredPolicyHash)] = RequiredPolicyHash is not null,
                [nameof(MaximumPositionValue)] = MaximumPositionValue is not null,
                [nameof(MaximumQuantity)] = MaximumQuantity is not null,
                [nameof(RequiredLevel)] = RequiredLevel is not null,
                [nameof(ValidUntil)] = ValidUntil is not null
            };
            var allowed = requiredProperties
                .Concat(optionalProperties)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var (name, present) in values)
            {
                if (present && !allowed.Contains(name))
                    throw new ArgumentException(
                        $"Payload property '{name}' is not valid for this condition kind.",
                        name);
            }

            foreach (var required in requiredProperties)
            {
                if (!values.TryGetValue(required, out var present) || !present)
                    throw new ArgumentException(
                        $"Payload property '{required}' is required for this condition kind.",
                        required);
            }
        }

        private static T Required<T>(T? value, string name) where T : struct =>
            value ?? throw new ArgumentException(
                $"Continuation condition property '{name}' is required.",
                name);

        private static string Required(string? value, string name) =>
            !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException(
                    $"Continuation condition property '{name}' is required.",
                    name);
    }
}
