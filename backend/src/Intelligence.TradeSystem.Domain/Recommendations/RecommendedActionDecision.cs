using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>Структурированное решение о действии над уже открытой позицией.</summary>
public sealed record RecommendedActionDecision
{
    public RecommendedActionDecision(
        PositionAction action,
        decimal confidence,
        RecommendationPriority priority,
        IEnumerable<ReasonCode> reasonCodes)
    {
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action), action, "Action must be defined.");
        ArgumentOutOfRangeException.ThrowIfNegative(confidence);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(confidence, 1m);
        if (!Enum.IsDefined(priority))
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Priority must be defined.");
        ArgumentNullException.ThrowIfNull(reasonCodes);

        var reasons = reasonCodes.ToArray();
        if (reasons.Length == 0)
            throw new ArgumentException("Action must contain at least one reason code.", nameof(reasonCodes));
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasonCodes), "Reason code must be defined.");
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Action reason codes cannot contain duplicates.", nameof(reasonCodes));

        Action = action;
        Confidence = confidence;
        Priority = priority;
        ReasonCodes = new ReadOnlyCollection<ReasonCode>(reasons);
    }

    private RecommendedActionDecision(PositionAction action, RecommendationPriority priority)
    {
        Action = action;
        Priority = priority;
        ReasonCodes = Array.Empty<ReasonCode>();
    }

    public PositionAction Action { get; }
    public decimal Confidence { get; }
    public RecommendationPriority Priority { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }

    internal static RecommendedActionDecision Legacy(
        PositionAction action,
        RecommendationPriority priority = RecommendationPriority.Normal) =>
        new(action, priority);
}
