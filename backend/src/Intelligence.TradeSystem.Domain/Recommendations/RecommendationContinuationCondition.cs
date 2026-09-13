using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

public enum RecommendationContinuationConditionScope
{
    Action,
    AddDecision,
    Recommendation
}

public enum RecommendationContinuationConditionKind
{
    TrendAlignment,
    MomentumReliability,
    MomentumState,
    MomentumAvailability,
    MomentumExhaustion,
    StopState,
    StopAvailability,
    StopRelativePosition,
    ProfitProtection,
    LiquidationState,
    LiquidationDistance,
    PnlThreshold,
    PnlAvailability,
    HigherPriorityActions,
    DataQuality,
    SafetyState,
    PortfolioRiskDecision,
    LowVolume,
    PolicyIdentity,
    AddAllowedCapacity,
    OpposingLevel,
    RecommendationExpiry,
    ContinuationContextUnavailable
}

public enum RecommendationPnlComparison
{
    AtLeast,
    AtMost
}

public enum RecommendationOpposingLevel
{
    ResistanceNearby,
    SupportNearby
}

/// <summary>
/// Базовый тип машинно-проверяемого условия продолжения рекомендации.
/// Условия не содержат delegates, expressions или свободный DSL.
/// </summary>
public abstract record RecommendationContinuationCondition
{
    protected RecommendationContinuationCondition(
        RecommendationContinuationConditionScope scope,
        RecommendationContinuationConditionKind kind)
    {
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Condition scope must be defined.");
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Condition kind must be defined.");

        Scope = scope;
        Kind = kind;
    }

    public RecommendationContinuationConditionScope Scope { get; }
    public RecommendationContinuationConditionKind Kind { get; }
}

public sealed record TrendAlignmentCondition : RecommendationContinuationCondition
{
    public TrendAlignmentCondition(
        RecommendationContinuationConditionScope scope,
        PositionTrendAlignment requiredAlignment)
        : base(scope, RecommendationContinuationConditionKind.TrendAlignment)
    {
        if (!Enum.IsDefined(requiredAlignment))
            throw new ArgumentOutOfRangeException(
                nameof(requiredAlignment),
                requiredAlignment,
                "Trend alignment must be defined.");

        RequiredAlignment = requiredAlignment;
    }

    public PositionTrendAlignment RequiredAlignment { get; }
}

public sealed record MomentumReliabilityCondition : RecommendationContinuationCondition
{
    public MomentumReliabilityCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredReliability)
        : base(scope, RecommendationContinuationConditionKind.MomentumReliability) =>
        RequiredReliability = requiredReliability;

    public bool RequiredReliability { get; }
}

public sealed record MomentumStateCondition : RecommendationContinuationCondition
{
    public MomentumStateCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentMomentumState requiredState)
        : base(scope, RecommendationContinuationConditionKind.MomentumState)
    {
        if (!Enum.IsDefined(requiredState))
            throw new ArgumentOutOfRangeException(
                nameof(requiredState),
                requiredState,
                "Momentum state must be defined.");

        RequiredState = requiredState;
    }

    public AssessmentMomentumState RequiredState { get; }
}

public sealed record MomentumAvailabilityCondition : RecommendationContinuationCondition
{
    public MomentumAvailabilityCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredAvailability)
        : base(scope, RecommendationContinuationConditionKind.MomentumAvailability) =>
        RequiredAvailability = requiredAvailability;

    public bool RequiredAvailability { get; }
}

public sealed record MomentumExhaustionCondition : RecommendationContinuationCondition
{
    public MomentumExhaustionCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredExhaustion)
        : base(scope, RecommendationContinuationConditionKind.MomentumExhaustion) =>
        RequiredExhaustion = requiredExhaustion;

    public bool RequiredExhaustion { get; }
}

public sealed record StopStateCondition : RecommendationContinuationCondition
{
    public StopStateCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentStopState requiredState)
        : base(scope, RecommendationContinuationConditionKind.StopState)
    {
        if (!Enum.IsDefined(requiredState))
            throw new ArgumentOutOfRangeException(
                nameof(requiredState),
                requiredState,
                "Stop state must be defined.");

        RequiredState = requiredState;
    }

    public AssessmentStopState RequiredState { get; }
}

public sealed record StopAvailabilityCondition : RecommendationContinuationCondition
{
    public StopAvailabilityCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredAvailability)
        : base(scope, RecommendationContinuationConditionKind.StopAvailability) =>
        RequiredAvailability = requiredAvailability;

    public bool RequiredAvailability { get; }
}

public sealed record StopRelativePositionCondition : RecommendationContinuationCondition
{
    public StopRelativePositionCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentPricePosition requiredPosition)
        : base(scope, RecommendationContinuationConditionKind.StopRelativePosition)
    {
        if (!Enum.IsDefined(requiredPosition))
            throw new ArgumentOutOfRangeException(
                nameof(requiredPosition),
                requiredPosition,
                "Stop relative position must be defined.");

        RequiredPosition = requiredPosition;
    }

    public AssessmentPricePosition RequiredPosition { get; }
}

public sealed record ProfitProtectionCondition : RecommendationContinuationCondition
{
    public ProfitProtectionCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredProtection)
        : base(scope, RecommendationContinuationConditionKind.ProfitProtection) =>
        RequiredProtection = requiredProtection;

    public bool RequiredProtection { get; }
}

public sealed record LiquidationStateCondition : RecommendationContinuationCondition
{
    public LiquidationStateCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentLiquidationState requiredState)
        : base(scope, RecommendationContinuationConditionKind.LiquidationState)
    {
        if (!Enum.IsDefined(requiredState))
            throw new ArgumentOutOfRangeException(
                nameof(requiredState),
                requiredState,
                "Liquidation state must be defined.");

        RequiredState = requiredState;
    }

    public AssessmentLiquidationState RequiredState { get; }
}

public sealed record LiquidationDistanceCondition : RecommendationContinuationCondition
{
    public LiquidationDistanceCondition(
        RecommendationContinuationConditionScope scope,
        decimal minimumDistancePercent)
        : base(scope, RecommendationContinuationConditionKind.LiquidationDistance)
    {
        if (minimumDistancePercent <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(minimumDistancePercent),
                minimumDistancePercent,
                "Minimum liquidation distance must be positive.");

        MinimumDistancePercent = minimumDistancePercent;
    }

    public decimal MinimumDistancePercent { get; }
}

public sealed record PnlThresholdCondition : RecommendationContinuationCondition
{
    public PnlThresholdCondition(
        RecommendationContinuationConditionScope scope,
        RecommendationPnlComparison comparison,
        decimal threshold)
        : base(scope, RecommendationContinuationConditionKind.PnlThreshold)
    {
        if (!Enum.IsDefined(comparison))
            throw new ArgumentOutOfRangeException(
                nameof(comparison),
                comparison,
                "PnL comparison must be defined.");

        Comparison = comparison;
        Threshold = threshold;
    }

    public RecommendationPnlComparison Comparison { get; }
    public decimal Threshold { get; }
}

public sealed record PnlAvailabilityCondition : RecommendationContinuationCondition
{
    public PnlAvailabilityCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredAvailability)
        : base(scope, RecommendationContinuationConditionKind.PnlAvailability) =>
        RequiredAvailability = requiredAvailability;

    public bool RequiredAvailability { get; }
}

public sealed record HigherPriorityActionsCondition : RecommendationContinuationCondition
{
    public HigherPriorityActionsCondition(
        RecommendationContinuationConditionScope scope,
        IEnumerable<PositionAction> requiredActions)
        : base(scope, RecommendationContinuationConditionKind.HigherPriorityActions)
    {
        if (scope is not RecommendationContinuationConditionScope.Action and
            not RecommendationContinuationConditionScope.AddDecision)
            throw new ArgumentException(
                "Higher-priority action conditions must use Action or AddDecision scope.",
                nameof(scope));
        ArgumentNullException.ThrowIfNull(requiredActions);

        var actions = requiredActions.ToArray();
        if (actions.Length == 0)
            throw new ArgumentException(
                "At least one higher-priority action is required.",
                nameof(requiredActions));
        if (actions.Any(action =>
                !Enum.IsDefined(action) ||
                action is PositionAction.Hold or PositionAction.Watch))
            throw new ArgumentException(
                "Higher-priority actions must be defined actionable decisions.",
                nameof(requiredActions));
        if (actions.Distinct().Count() != actions.Length)
            throw new ArgumentException(
                "Higher-priority actions cannot contain duplicates.",
                nameof(requiredActions));

        RequiredActions = Array.AsReadOnly(actions);
    }

    public IReadOnlyList<PositionAction> RequiredActions { get; }
}

public sealed record DataQualityCondition : RecommendationContinuationCondition
{
    public DataQualityCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentDataQuality requiredQuality)
        : base(scope, RecommendationContinuationConditionKind.DataQuality)
    {
        if (!Enum.IsDefined(requiredQuality))
            throw new ArgumentOutOfRangeException(
                nameof(requiredQuality),
                requiredQuality,
                "Data quality must be defined.");

        RequiredQuality = requiredQuality;
    }

    public AssessmentDataQuality RequiredQuality { get; }
}

public sealed record SafetyStateCondition : RecommendationContinuationCondition
{
    public SafetyStateCondition(
        RecommendationContinuationConditionScope scope,
        AssessmentSafetyState requiredState)
        : base(scope, RecommendationContinuationConditionKind.SafetyState)
    {
        if (!Enum.IsDefined(requiredState))
            throw new ArgumentOutOfRangeException(
                nameof(requiredState),
                requiredState,
                "Safety state must be defined.");

        RequiredState = requiredState;
    }

    public AssessmentSafetyState RequiredState { get; }
}

public sealed record PortfolioRiskDecisionCondition : RecommendationContinuationCondition
{
    public PortfolioRiskDecisionCondition(
        RecommendationContinuationConditionScope scope,
        RiskIncreaseDecision requiredDecision)
        : base(scope, RecommendationContinuationConditionKind.PortfolioRiskDecision)
    {
        if (!Enum.IsDefined(requiredDecision))
            throw new ArgumentOutOfRangeException(
                nameof(requiredDecision),
                requiredDecision,
                "Risk decision must be defined.");

        RequiredDecision = requiredDecision;
    }

    public RiskIncreaseDecision RequiredDecision { get; }
}

public sealed record LowVolumeCondition : RecommendationContinuationCondition
{
    public LowVolumeCondition(
        RecommendationContinuationConditionScope scope,
        bool requiredLowVolume)
        : base(scope, RecommendationContinuationConditionKind.LowVolume) =>
        RequiredLowVolume = requiredLowVolume;

    public bool RequiredLowVolume { get; }
}

public sealed record PolicyIdentityCondition : RecommendationContinuationCondition
{
    public PolicyIdentityCondition(
        RecommendationContinuationConditionScope scope,
        PolicyConfigurationIdentity requiredIdentity)
        : base(scope, RecommendationContinuationConditionKind.PolicyIdentity)
    {
        if (scope != RecommendationContinuationConditionScope.Recommendation)
            throw new ArgumentException(
                "Policy identity conditions must use Recommendation scope.",
                nameof(scope));
        if (string.IsNullOrWhiteSpace(requiredIdentity.Version) ||
            string.IsNullOrWhiteSpace(requiredIdentity.Hash))
            throw new ArgumentException("Policy identity must contain version and hash.", nameof(requiredIdentity));

        RequiredIdentity = requiredIdentity;
    }

    public PolicyConfigurationIdentity RequiredIdentity { get; }
}

public sealed record AddAllowedCapacityCondition : RecommendationContinuationCondition
{
    public AddAllowedCapacityCondition(
        decimal maximumPositionValue,
        decimal? maximumQuantity)
        : base(
            RecommendationContinuationConditionScope.AddDecision,
            RecommendationContinuationConditionKind.AddAllowedCapacity)
    {
        if (maximumPositionValue <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(maximumPositionValue),
                maximumPositionValue,
                "Maximum position value must be positive.");
        if (maximumQuantity is <= 0m)
            throw new ArgumentOutOfRangeException(
                nameof(maximumQuantity),
                maximumQuantity,
                "Maximum quantity must be positive when supplied.");

        MaximumPositionValue = maximumPositionValue;
        MaximumQuantity = maximumQuantity;
    }

    public decimal MaximumPositionValue { get; }
    public decimal? MaximumQuantity { get; }
}

public sealed record OpposingLevelCondition : RecommendationContinuationCondition
{
    public OpposingLevelCondition(
        RecommendationContinuationConditionScope scope,
        RecommendationOpposingLevel requiredLevel)
        : base(scope, RecommendationContinuationConditionKind.OpposingLevel)
    {
        if (!Enum.IsDefined(requiredLevel))
            throw new ArgumentOutOfRangeException(
                nameof(requiredLevel),
                requiredLevel,
                "Opposing level must be defined.");

        RequiredLevel = requiredLevel;
    }

    public RecommendationOpposingLevel RequiredLevel { get; }
}

public sealed record RecommendationExpiryCondition : RecommendationContinuationCondition
{
    public RecommendationExpiryCondition(DateTimeOffset validUntil)
        : base(
            RecommendationContinuationConditionScope.Recommendation,
            RecommendationContinuationConditionKind.RecommendationExpiry)
    {
        if (validUntil == default)
            throw new ArgumentException("Expiry timestamp must be initialized.", nameof(validUntil));

        ValidUntil = validUntil;
    }

    public DateTimeOffset ValidUntil { get; }
}

public sealed record ContinuationContextUnavailableCondition : RecommendationContinuationCondition
{
    public ContinuationContextUnavailableCondition()
        : base(
            RecommendationContinuationConditionScope.Recommendation,
            RecommendationContinuationConditionKind.ContinuationContextUnavailable)
    {
    }
}
