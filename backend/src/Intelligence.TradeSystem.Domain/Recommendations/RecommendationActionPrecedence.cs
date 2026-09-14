using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

internal static class RecommendationActionPrecedence
{
    public static bool IsHigherPriority(PositionAction current, PositionAction candidate) =>
        GetHigherPriorityActions(current).Contains(candidate);

    public static PositionAction[] GetHigherPriorityActions(PositionAction action) =>
        action switch
        {
            PositionAction.Hold or PositionAction.Watch =>
                [
                    PositionAction.Close,
                    PositionAction.Reduce,
                    PositionAction.TakePartialProfit,
                    PositionAction.MoveStop,
                    PositionAction.ProtectProfit
                ],
            PositionAction.ProtectProfit =>
                [
                    PositionAction.Close,
                    PositionAction.Reduce,
                    PositionAction.TakePartialProfit,
                    PositionAction.MoveStop
                ],
            PositionAction.MoveStop =>
                [
                    PositionAction.Close,
                    PositionAction.Reduce,
                    PositionAction.TakePartialProfit
                ],
            PositionAction.TakePartialProfit =>
                [PositionAction.Close, PositionAction.Reduce],
            PositionAction.Reduce =>
                [PositionAction.Close],
            PositionAction.Close => [],
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Action must be defined.")
        };
}
