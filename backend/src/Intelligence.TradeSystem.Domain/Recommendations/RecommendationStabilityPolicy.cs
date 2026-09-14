using System.Diagnostics.CodeAnalysis;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Чистая детерминированная политика публикации нового recommendation candidate.
/// </summary>
public sealed class RecommendationStabilityPolicy
{
    [SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "The policy is registered as a stateless singleton service.")]
    public RecommendationStabilityEvaluation Evaluate(
        Recommendation? current,
        RecommendationPolicyEvaluation candidate,
        RecommendationStabilityState? pendingState,
        RecommendationStabilityProfile profile,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(profile);
        if (asOf == default)
            throw new ArgumentException("AsOf must be initialized.", nameof(asOf));
        if (candidate.CreatedAt > asOf)
            throw new ArgumentOutOfRangeException(
                nameof(asOf),
                asOf,
                "Stability evaluation cannot precede candidate creation.");
        if (asOf >= candidate.ValidUntil)
            throw new ArgumentException(
                "Candidate must be valid at stability evaluation time.",
                nameof(candidate));
        if (pendingState is not null &&
            asOf < pendingState.LastObservedAt)
            throw new ArgumentOutOfRangeException(
                nameof(asOf),
                asOf,
                "Stability observations must be chronological.");

        var candidateSemantic = RecommendationSemanticState.From(candidate);
        if (pendingState is not null &&
            asOf == pendingState.LastObservedAt &&
            !pendingState.SemanticState.Equals(candidateSemantic))
            throw new ArgumentException(
                "Different stability candidates cannot share the same observation timestamp.",
                nameof(candidate));

        if (current is null)
            return Publish(RecommendationStabilityReason.NoCurrentRecommendation);

        if (asOf < current.CreatedAt)
            throw new ArgumentOutOfRangeException(
                nameof(asOf),
                asOf,
                "Stability evaluation cannot precede current recommendation creation.");

        var currentSemantic = RecommendationSemanticState.From(current);
        if (current.Status is RecommendationStatus.Active or RecommendationStatus.Acknowledged)
        {
            if (candidate.CreatedAt <= current.CreatedAt)
                throw new ArgumentException(
                    "A replacement candidate must be newer than the current recommendation.",
                    nameof(candidate));
            if (current.Status == RecommendationStatus.Acknowledged &&
                current.AcknowledgedAt is { } acknowledgedAt &&
                candidate.CreatedAt < acknowledgedAt)
                throw new ArgumentException(
                    "A replacement candidate cannot precede recommendation acknowledgement.",
                    nameof(candidate));
        }

        if (current.Status is RecommendationStatus.Dismissed or RecommendationStatus.Superseded)
            return Publish(RecommendationStabilityReason.CurrentInactive);
        if (current.Status == RecommendationStatus.Expired || asOf >= current.ValidUntil)
            return Publish(RecommendationStabilityReason.CurrentExpired);
        if (currentSemantic.Equals(candidateSemantic))
            return KeepExisting(RecommendationStabilityReason.Duplicate);

        if (current.AddDecision == AddDecision.AddAllowed &&
            candidate.AddDecision.Decision == AddDecision.DoNotAdd)
            return Publish(RecommendationStabilityReason.AddPermissionRevoked);
        if (RecommendationActionPredicates.IsSafetyBlocked(candidate))
            return Publish(RecommendationStabilityReason.SafetyEscalation);

        var capacityChange = current.AddDecision == AddDecision.AddAllowed &&
            candidate.AddDecision.Decision == AddDecision.AddAllowed
            ? ClassifyCapacityChange(currentSemantic, candidateSemantic)
            : CapacityChange.Equal;
        if (capacityChange == CapacityChange.Decrease)
            return Publish(RecommendationStabilityReason.RiskReduction);

        var isRiskIncreasingTransition =
            current.AddDecision != AddDecision.AddAllowed &&
            candidate.AddDecision.Decision == AddDecision.AddAllowed;
        if (capacityChange is CapacityChange.Increase or CapacityChange.Mixed)
            isRiskIncreasingTransition = true;
        if (RecommendationStabilityTransitionClassifier.IsLessProtectiveTransition(
                current.RecommendedAction,
                candidate.Action.Action))
            isRiskIncreasingTransition = true;

        if (!isRiskIncreasingTransition &&
            RecommendationStabilityTransitionClassifier.IsImmediateRiskReduction(
                current.RecommendedAction,
                candidate.Action.Action))
            return Publish(RecommendationStabilityReason.RiskReduction);

        var policyChanged = current.PolicyIdentity != candidate.PolicyIdentity;
        if (policyChanged && !isRiskIncreasingTransition)
            return Publish(RecommendationStabilityReason.PolicyChanged);
        if (!isRiskIncreasingTransition &&
            candidate.Action.Priority == RecommendationPriority.Critical &&
            current.ActionDecision.Priority != RecommendationPriority.Critical)
            return Publish(RecommendationStabilityReason.PriorityEscalation);

        var confirmationPeriod = profile.ImprovementConfirmationPeriod;
        var confirmationObservations = profile.ImprovementConfirmationObservations;
        var transitionReason = RecommendationStabilityReason.MaterialChange;
        if (current.AddDecision != AddDecision.AddAllowed &&
            candidate.AddDecision.Decision == AddDecision.AddAllowed)
        {
            confirmationPeriod = profile.AddAllowedConfirmationPeriod;
            confirmationObservations = profile.AddAllowedConfirmationObservations;
            transitionReason = RecommendationStabilityReason.AddPermissionGranted;
        }
        else if (current.AddDecision == AddDecision.AddAllowed &&
            candidate.AddDecision.Decision == AddDecision.AddAllowed)
        {
            if (capacityChange is CapacityChange.Increase or CapacityChange.Mixed)
            {
                confirmationPeriod = profile.AddAllowedConfirmationPeriod;
                confirmationObservations = profile.AddAllowedConfirmationObservations;
                transitionReason = RecommendationStabilityReason.AddPermissionGranted;
            }
        }

        var nextState = CreateNextState(
            candidateSemantic,
            pendingState,
            asOf,
            out var pendingCandidateChanged);
        if (!HasConfirmation(nextState, confirmationPeriod, confirmationObservations))
        {
            var pendingReason = transitionReason == RecommendationStabilityReason.AddPermissionGranted
                ? transitionReason
                : pendingCandidateChanged
                    ? RecommendationStabilityReason.CandidateChanged
                    : RecommendationStabilityReason.AwaitingConfirmation;
            return Pending(pendingReason, nextState);
        }

        if (!HasElapsed(current.CreatedAt, asOf, profile.MinimumReplacementInterval))
            return Pending(RecommendationStabilityReason.WithinCooldown, nextState);

        return Publish(transitionReason);
    }

    private static RecommendationStabilityEvaluation KeepExisting(RecommendationStabilityReason reason) =>
        new(
            new RecommendationStabilityDecision(
                RecommendationStabilityDecisionKind.KeepExisting,
                reason),
            null);

    private static RecommendationStabilityEvaluation Pending(
        RecommendationStabilityReason reason,
        RecommendationStabilityState nextState) =>
        new(
            new RecommendationStabilityDecision(
                RecommendationStabilityDecisionKind.PendingConfirmation,
                reason),
            nextState);

    private static RecommendationStabilityEvaluation Publish(RecommendationStabilityReason reason) =>
        new(
            new RecommendationStabilityDecision(
                RecommendationStabilityDecisionKind.PublishCandidate,
                reason),
            null);

    private static RecommendationStabilityState CreateNextState(
        RecommendationSemanticState candidate,
        RecommendationStabilityState? pendingState,
        DateTimeOffset asOf,
        out bool pendingCandidateChanged)
    {
        if (pendingState is not null && pendingState.SemanticState.Equals(candidate))
        {
            pendingCandidateChanged = false;
            if (asOf == pendingState.LastObservedAt)
                return pendingState;

            return new(
                pendingState.SemanticState,
                pendingState.FirstObservedAt,
                asOf,
                checked(pendingState.ConsecutiveObservations + 1));
        }
        pendingCandidateChanged = pendingState is not null;
        return new(candidate, asOf, asOf, 1);
    }

    private static bool HasConfirmation(
        RecommendationStabilityState state,
        TimeSpan confirmationPeriod,
        int confirmationObservations) =>
        state.ConsecutiveObservations >= confirmationObservations &&
        HasElapsed(state.FirstObservedAt, state.LastObservedAt, confirmationPeriod);

    private static bool HasElapsed(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeSpan duration) =>
        end >= start && end - start >= duration;

    private static CapacityChange ClassifyCapacityChange(
        RecommendationSemanticState current,
        RecommendationSemanticState candidate)
    {
        var valueChange = CompareLimit(
            current.MaximumAdditionalPositionValue,
            candidate.MaximumAdditionalPositionValue);
        var quantityChange = CompareLimit(
            current.MaximumAdditionalQuantity,
            candidate.MaximumAdditionalQuantity);
        if ((valueChange < 0 && quantityChange > 0) ||
            (valueChange > 0 && quantityChange < 0))
            return CapacityChange.Mixed;
        if (valueChange < 0 || quantityChange < 0)
            return CapacityChange.Decrease;
        if (valueChange > 0 || quantityChange > 0)
            return CapacityChange.Increase;
        return CapacityChange.Equal;
    }

    private static int CompareLimit(decimal? current, decimal? candidate)
    {
        if (current == candidate)
            return 0;
        if (current is null)
            return 0;
        if (candidate is null)
            return -1;
        return candidate.Value.CompareTo(current.Value);
    }

    private enum CapacityChange
    {
        Equal,
        Decrease,
        Increase,
        Mixed
    }
}
