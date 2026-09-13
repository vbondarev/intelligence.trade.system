using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Настраиваемые интервалы повторной оценки. Safety-инварианты находятся в policy/evaluator
/// и не могут быть отключены этим профилем.
/// </summary>
public sealed record RecommendationReevaluationProfile
{
    public RecommendationReevaluationProfile(
        TimeSpan hold,
        TimeSpan watch,
        TimeSpan protectProfit,
        TimeSpan reduce,
        TimeSpan close,
        TimeSpan moveStop,
        TimeSpan takePartialProfit,
        TimeSpan addAllowed,
        TimeSpan validityPeriod)
    {
        Validate(hold, nameof(hold), validityPeriod);
        Validate(watch, nameof(watch), validityPeriod);
        Validate(protectProfit, nameof(protectProfit), validityPeriod);
        Validate(reduce, nameof(reduce), validityPeriod);
        Validate(close, nameof(close), validityPeriod);
        Validate(moveStop, nameof(moveStop), validityPeriod);
        Validate(takePartialProfit, nameof(takePartialProfit), validityPeriod);
        Validate(addAllowed, nameof(addAllowed), validityPeriod);

        Hold = hold;
        Watch = watch;
        ProtectProfit = protectProfit;
        Reduce = reduce;
        Close = close;
        MoveStop = moveStop;
        TakePartialProfit = takePartialProfit;
        AddAllowed = addAllowed;
    }

    public TimeSpan Hold { get; }
    public TimeSpan Watch { get; }
    public TimeSpan ProtectProfit { get; }
    public TimeSpan Reduce { get; }
    public TimeSpan Close { get; }
    public TimeSpan MoveStop { get; }
    public TimeSpan TakePartialProfit { get; }
    public TimeSpan AddAllowed { get; }

    public TimeSpan For(PositionAction action) => action switch
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

    public TimeSpan EffectiveInterval(PositionAction action, bool addAllowed) =>
        addAllowed ? Min(For(action), AddAllowed) : For(action);

    internal void ValidateAgainst(TimeSpan validityPeriod)
    {
        if (validityPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(validityPeriod),
                validityPeriod,
                "Validity period must be positive.");
        Validate(Hold, nameof(Hold), validityPeriod);
        Validate(Watch, nameof(Watch), validityPeriod);
        Validate(ProtectProfit, nameof(ProtectProfit), validityPeriod);
        Validate(Reduce, nameof(Reduce), validityPeriod);
        Validate(Close, nameof(Close), validityPeriod);
        Validate(MoveStop, nameof(MoveStop), validityPeriod);
        Validate(TakePartialProfit, nameof(TakePartialProfit), validityPeriod);
        Validate(AddAllowed, nameof(AddAllowed), validityPeriod);
    }

    public static RecommendationReevaluationProfile Default => new(
        hold: TimeSpan.FromMinutes(4),
        watch: TimeSpan.FromMinutes(2),
        protectProfit: TimeSpan.FromMinutes(2),
        reduce: TimeSpan.FromMinutes(1),
        close: TimeSpan.FromMinutes(1),
        moveStop: TimeSpan.FromMinutes(2),
        takePartialProfit: TimeSpan.FromMinutes(2),
        addAllowed: TimeSpan.FromMinutes(2),
        validityPeriod: TimeSpan.FromMinutes(5));

    public static RecommendationReevaluationProfile CreateDefault(TimeSpan validityPeriod)
    {
        if (validityPeriod <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(validityPeriod),
                validityPeriod,
                "Validity period must be positive.");

        var defaultInterval = validityPeriod.Ticks == 1
            ? validityPeriod
            : validityPeriod - TimeSpan.FromTicks(Math.Max(1, validityPeriod.Ticks / 5));
        var frequentInterval = TimeSpan.FromTicks(Math.Max(1, validityPeriod.Ticks / 2));
        return new(
            defaultInterval,
            frequentInterval,
            frequentInterval,
            frequentInterval,
            frequentInterval,
            frequentInterval,
            frequentInterval,
            frequentInterval,
            validityPeriod);
    }

    private static void Validate(TimeSpan value, string parameterName, TimeSpan validityPeriod)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, value, "Reevaluation interval must be positive.");
        if (value > validityPeriod)
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Reevaluation interval cannot exceed the policy validity period.");
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second) =>
        first <= second ? first : second;
}





