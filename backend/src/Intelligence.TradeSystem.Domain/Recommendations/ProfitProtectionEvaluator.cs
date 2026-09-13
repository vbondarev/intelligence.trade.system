using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Единая семантика стопа, который действительно защищает уже полученную прибыль.
/// </summary>
public static class ProfitProtectionEvaluator
{
    public static bool IsStopProtectingProfit(PositionAssessmentResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Stop.State != AssessmentStopState.Protective)
            return false;

        return result.PositionSide switch
        {
            PositionSide.Long => result.Stop.PriceRelativeToEntry == AssessmentPricePosition.Above,
            PositionSide.Short => result.Stop.PriceRelativeToEntry == AssessmentPricePosition.Below,
            _ => false
        };
    }
}
