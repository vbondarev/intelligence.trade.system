using System.Collections.ObjectModel;

namespace Intelligence.TradeSystem.Domain.Portfolio;

public sealed record RiskIncreasePolicyResult
{
    public RiskIncreasePolicyResult(RiskIncreaseDecision decision, IEnumerable<ReasonCode> reasonCodes)
    {
        ArgumentNullException.ThrowIfNull(reasonCodes);
        Decision = decision;
        ReasonCodes = new ReadOnlyCollection<ReasonCode>(reasonCodes.Distinct().ToArray());
    }

    public RiskIncreaseDecision Decision { get; }
    public IReadOnlyList<ReasonCode> ReasonCodes { get; }
}
