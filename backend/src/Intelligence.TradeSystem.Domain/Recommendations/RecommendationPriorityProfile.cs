using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>Детерминированный priority-профиль для всех действий политики.</summary>
public sealed record RecommendationPriorityProfile
{
    public RecommendationPriorityProfile(
        RecommendationPriority hold,
        RecommendationPriority watch,
        RecommendationPriority protectProfit,
        RecommendationPriority reduce,
        RecommendationPriority close,
        RecommendationPriority moveStop,
        RecommendationPriority takePartialProfit)
    {
        Validate(hold, nameof(hold));
        Validate(watch, nameof(watch));
        Validate(protectProfit, nameof(protectProfit));
        Validate(reduce, nameof(reduce));
        Validate(close, nameof(close));
        Validate(moveStop, nameof(moveStop));
        Validate(takePartialProfit, nameof(takePartialProfit));

        Hold = hold;
        Watch = watch;
        ProtectProfit = protectProfit;
        Reduce = reduce;
        Close = close;
        MoveStop = moveStop;
        TakePartialProfit = takePartialProfit;
    }

    public RecommendationPriority Hold { get; }
    public RecommendationPriority Watch { get; }
    public RecommendationPriority ProtectProfit { get; }
    public RecommendationPriority Reduce { get; }
    public RecommendationPriority Close { get; }
    public RecommendationPriority MoveStop { get; }
    public RecommendationPriority TakePartialProfit { get; }

    public RecommendationPriority For(PositionAction action) => action switch
    {
        PositionAction.Hold => Hold,
        PositionAction.Watch => Watch,
        PositionAction.ProtectProfit => ProtectProfit,
        PositionAction.Reduce => Reduce,
        PositionAction.Close => Close,
        PositionAction.MoveStop => MoveStop,
        PositionAction.TakePartialProfit => TakePartialProfit,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Action must be defined.")
    };

    public static RecommendationPriorityProfile Default => new(
        RecommendationPriority.Normal,
        RecommendationPriority.Normal,
        RecommendationPriority.High,
        RecommendationPriority.High,
        RecommendationPriority.Critical,
        RecommendationPriority.High,
        RecommendationPriority.High);

    private static void Validate(RecommendationPriority value, string parameterName)
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName, value, "Priority must be defined.");
    }
}
