using System.Collections.ObjectModel;
using Intelligence.TradeSystem.Domain.Decisions;

namespace Intelligence.TradeSystem.Domain.Portfolio;

public sealed record RiskIncreasePolicyResult
{
    private RiskIncreasePolicyResult(RiskIncreaseDecision decision, IReadOnlyList<ReasonCode> reasonCodes)
    {
        Decision = decision;
        ReasonCodes = reasonCodes;
    }

    public RiskIncreaseDecision Decision { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }

    public static RiskIncreasePolicyResult Allowed() =>
        new(RiskIncreaseDecision.Allowed,
            new ReadOnlyCollection<ReasonCode>([ReasonCode.RiskWithinLimits]));

    public static RiskIncreasePolicyResult Blocked(IEnumerable<ReasonCode> reasonCodes)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);

        var distinctReasons = reasonCodes.Distinct().ToArray();
        if (distinctReasons.Length == 0)
            throw new ArgumentException("At least one blocking reason is required.", nameof(reasonCodes));
        if (distinctReasons.Any(reason => !Enum.IsDefined(reason)))
            throw new ArgumentOutOfRangeException(nameof(reasonCodes), "Reason code must be defined.");
        if (distinctReasons.Any(reason => !ReasonCodeClassification.IsPortfolioRiskReason(reason)))
            throw new ArgumentException(
                "Blocked results can contain only portfolio risk reasons.", nameof(reasonCodes));
        if (distinctReasons.Contains(ReasonCode.RiskWithinLimits))
            throw new ArgumentException(
                "Blocked results cannot contain RiskWithinLimits.", nameof(reasonCodes));

        return new RiskIncreasePolicyResult(
            RiskIncreaseDecision.Blocked,
            new ReadOnlyCollection<ReasonCode>(distinctReasons));
    }
}
