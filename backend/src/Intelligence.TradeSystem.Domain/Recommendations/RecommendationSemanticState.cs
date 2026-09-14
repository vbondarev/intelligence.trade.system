using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Пользовательски значимое содержимое recommendation без технических metadata.
/// </summary>
public sealed class RecommendationSemanticState : IEquatable<RecommendationSemanticState>
{
    public RecommendationSemanticState(
        PositionAction positionAction,
        AddDecision addDecision,
        RecommendationPriority priority,
        IEnumerable<ReasonCode> actionReasonCodes,
        IEnumerable<ReasonCode> addReasonCodes,
        PolicyConfigurationIdentity policyIdentity,
        decimal? maximumAdditionalPositionValue = null,
        decimal? maximumAdditionalQuantity = null,
        IEnumerable<ReasonCode>? inheritedReasonCodes = null)
    {
        if (!Enum.IsDefined(positionAction))
            throw new ArgumentOutOfRangeException(nameof(positionAction), positionAction, "Action must be defined.");
        if (!Enum.IsDefined(addDecision))
            throw new ArgumentOutOfRangeException(nameof(addDecision), addDecision, "Add decision must be defined.");
        if (!Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be defined.");
        ArgumentNullException.ThrowIfNull(actionReasonCodes);
        ArgumentNullException.ThrowIfNull(addReasonCodes);
        inheritedReasonCodes ??= [];
        if (string.IsNullOrWhiteSpace(policyIdentity.Version) ||
            string.IsNullOrWhiteSpace(policyIdentity.Hash))
            throw new ArgumentException("Policy identity must contain version and hash.", nameof(policyIdentity));

        var actionReasons = actionReasonCodes.ToArray();
        var addReasons = addReasonCodes.ToArray();
        var inheritedReasons = inheritedReasonCodes.ToArray();
        if (actionReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(actionReasonCodes), "Reason code must be defined.");
        if (addReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(addReasonCodes), "Reason code must be defined.");
        if (inheritedReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(inheritedReasonCodes), "Reason code must be defined.");
        if (inheritedReasons.Any(reason => !ReasonCodeClassification.IsPortfolioRiskReason(reason)))
            throw new ArgumentException(
                "Inherited reasons must be portfolio-risk reason codes.",
                nameof(inheritedReasonCodes));
        if (inheritedReasons.Distinct().Count() != inheritedReasons.Length)
            throw new ArgumentException(
                "Inherited reason codes cannot contain duplicates.",
                nameof(inheritedReasonCodes));

        switch (addDecision)
        {
            case AddDecision.AddAllowed:
                if (maximumAdditionalPositionValue is not > 0m)
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumAdditionalPositionValue),
                        "AddAllowed requires a positive maximum position value.");
                if (maximumAdditionalQuantity is <= 0m)
                    throw new ArgumentOutOfRangeException(
                        nameof(maximumAdditionalQuantity),
                        "Maximum quantity must be positive when supplied.");
                break;
            case AddDecision.DoNotAdd:
            case AddDecision.NotEvaluated:
                if (maximumAdditionalPositionValue is not null || maximumAdditionalQuantity is not null)
                    throw new ArgumentException(
                        "Only AddAllowed may contain maximum size.",
                        nameof(maximumAdditionalPositionValue));
                break;
        }

        PositionAction = positionAction;
        AddDecision = addDecision;
        Priority = priority;
        ActionReasonCodes = Normalize(actionReasons);
        AddReasonCodes = Normalize(addReasons);
        InheritedReasonCodes = Normalize(inheritedReasons);
        PolicyIdentity = policyIdentity;
        MaximumAdditionalPositionValue = maximumAdditionalPositionValue;
        MaximumAdditionalQuantity = maximumAdditionalQuantity;
    }

    public PositionAction PositionAction { get; }
    public PositionAction Action => PositionAction;
    public AddDecision AddDecision { get; }
    public RecommendationPriority Priority { get; }
    public IReadOnlyList<ReasonCode> ActionReasonCodes { get; }
    public IReadOnlyList<ReasonCode> AddReasonCodes { get; }
    public IReadOnlyList<ReasonCode> InheritedReasonCodes { get; }
    public PolicyConfigurationIdentity PolicyIdentity { get; }
    public PolicyConfigurationIdentity PolicyConfigurationIdentity => PolicyIdentity;
    public decimal? MaximumAdditionalPositionValue { get; }
    public decimal? MaximumAdditionalQuantity { get; }

    public static RecommendationSemanticState From(Recommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        return new(
            recommendation.RecommendedAction,
            recommendation.AddDecision,
            recommendation.ActionDecision.Priority,
            recommendation.ActionReasonCodes,
            recommendation.AddReasonCodes,
            recommendation.PolicyIdentity,
            recommendation.MaximumAdditionalPositionValue,
            recommendation.MaximumAdditionalQuantity,
            recommendation.ReasonCodes.Where(ReasonCodeClassification.IsPortfolioRiskReason));
    }

    public static RecommendationSemanticState From(RecommendationPolicyEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        return new(
            evaluation.Action.Action,
            evaluation.AddDecision.Decision,
            evaluation.Action.Priority,
            evaluation.Action.ReasonCodes,
            evaluation.AddDecision.ReasonCodes,
            evaluation.PolicyIdentity,
            evaluation.AddDecision.MaximumAdditionalPositionValue,
            evaluation.AddDecision.MaximumAdditionalQuantity,
            evaluation.InheritedReasonCodes);
    }

    public bool Equals(RecommendationSemanticState? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        return other is not null &&
            PositionAction == other.PositionAction &&
            AddDecision == other.AddDecision &&
            Priority == other.Priority &&
            PolicyIdentity == other.PolicyIdentity &&
            MaximumAdditionalPositionValue == other.MaximumAdditionalPositionValue &&
            MaximumAdditionalQuantity == other.MaximumAdditionalQuantity &&
            ActionReasonCodes.SequenceEqual(other.ActionReasonCodes) &&
            AddReasonCodes.SequenceEqual(other.AddReasonCodes) &&
            InheritedReasonCodes.SequenceEqual(other.InheritedReasonCodes);
    }

    public override bool Equals(object? obj) =>
        obj is RecommendationSemanticState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PositionAction);
        hash.Add(AddDecision);
        hash.Add(Priority);
        hash.Add(PolicyIdentity);
        hash.Add(MaximumAdditionalPositionValue);
        hash.Add(MaximumAdditionalQuantity);
        foreach (var reason in ActionReasonCodes)
            hash.Add(reason);
        foreach (var reason in AddReasonCodes)
            hash.Add(reason);
        foreach (var reason in InheritedReasonCodes)
            hash.Add(reason);
        return hash.ToHashCode();
    }

    private static ReadOnlyCollection<ReasonCode> Normalize(IEnumerable<ReasonCode> reasons) =>
        new ReadOnlyCollection<ReasonCode>(
            reasons
                .Distinct()
                .OrderBy(reason => (int)reason)
                .ToArray());
}
