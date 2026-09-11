using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>Структурированный результат независимой проверки увеличения риска.</summary>
public sealed record AddDecisionResult
{
    public AddDecisionResult(
        AddDecision decision,
        IEnumerable<ReasonCode> reasonCodes,
        decimal? maximumAdditionalPositionValue,
        decimal? maximumAdditionalQuantity,
        AddDecisionConditions? conditions)
    {
        if (!Enum.IsDefined(decision))
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "Add decision must be defined.");
        ArgumentNullException.ThrowIfNull(reasonCodes);

        var reasons = reasonCodes.ToArray();
        if (reasons.Length == 0)
            throw new ArgumentException("Add decision must contain at least one reason code.", nameof(reasonCodes));
        if (reasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasonCodes), "Reason code must be defined.");
        if (reasons.Distinct().Count() != reasons.Length)
            throw new ArgumentException("Add reason codes cannot contain duplicates.", nameof(reasonCodes));

        switch (decision)
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
                ArgumentNullException.ThrowIfNull(conditions);
                break;
            case AddDecision.DoNotAdd:
            case AddDecision.NotEvaluated:
                if (maximumAdditionalPositionValue is not null ||
                    maximumAdditionalQuantity is not null ||
                    conditions is not null)
                    throw new ArgumentException(
                        "Only AddAllowed may contain maximum size and conditions.",
                        nameof(maximumAdditionalPositionValue));
                break;
        }

        Decision = decision;
        ReasonCodes = new ReadOnlyCollection<ReasonCode>(reasons);
        MaximumAdditionalPositionValue = maximumAdditionalPositionValue;
        MaximumAdditionalQuantity = maximumAdditionalQuantity;
        Conditions = conditions;
    }

    public AddDecision Decision { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }
    public decimal? MaximumAdditionalPositionValue { get; }
    public decimal? MaximumAdditionalQuantity { get; }
    public AddDecisionConditions? Conditions { get; }

    internal static AddDecisionResult Legacy(AddDecision decision) =>
        new(
            decision,
            new ReadOnlyCollection<ReasonCode>([ReasonCode.AddBlockedByAction]),
            null,
            null,
            null,
            legacy: true);

    private AddDecisionResult(
        AddDecision decision,
        IReadOnlyList<ReasonCode> reasonCodes,
        decimal? maximumAdditionalPositionValue,
        decimal? maximumAdditionalQuantity,
        AddDecisionConditions? conditions,
        bool legacy)
    {
        Decision = decision;
        ReasonCodes = reasonCodes;
        MaximumAdditionalPositionValue = maximumAdditionalPositionValue;
        MaximumAdditionalQuantity = maximumAdditionalQuantity;
        Conditions = conditions;
    }
}
