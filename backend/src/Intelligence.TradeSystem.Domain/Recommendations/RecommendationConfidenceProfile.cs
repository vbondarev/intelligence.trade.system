using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Детерминированные базовые confidence-профили для всех действий политики.
/// </summary>
public sealed record RecommendationConfidenceProfile
{
    public RecommendationConfidenceProfile(
        decimal hold,
        decimal watch,
        decimal protectProfit,
        decimal reduce,
        decimal close,
        decimal moveStop,
        decimal takePartialProfit)
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

    public decimal Hold { get; }
    public decimal Watch { get; }
    public decimal ProtectProfit { get; }
    public decimal Reduce { get; }
    public decimal Close { get; }
    public decimal MoveStop { get; }
    public decimal TakePartialProfit { get; }

    public decimal For(PositionAction action) => action switch
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

    public static RecommendationConfidenceProfile Default => new(
        hold: 0.70m,
        watch: 0.80m,
        protectProfit: 0.85m,
        reduce: 0.90m,
        close: 0.98m,
        moveStop: 0.88m,
        takePartialProfit: 0.86m);

    private static void Validate(decimal value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1m, parameterName);
    }
}
