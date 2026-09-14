using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

internal static class RecommendationStabilityTransitionClassifier
{
    public static bool IsImmediateRiskReduction(
        PositionAction current,
        PositionAction candidate) =>
        current != candidate &&
        (current == PositionAction.Hold && candidate == PositionAction.Watch ||
         RecommendationActionPrecedence.IsHigherPriority(current, candidate));

    public static bool IsLessProtectiveTransition(
        PositionAction current,
        PositionAction candidate) =>
        current != candidate &&
        (current == PositionAction.Watch && candidate == PositionAction.Hold ||
         RecommendationActionPrecedence.IsHigherPriority(candidate, current));
}
