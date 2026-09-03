namespace Intelligence.TradeSystem.Domain.Portfolio;

/// <summary>
/// Детерминированная политика текущих портфельных ограничений.
/// Allowed означает только отсутствие запрета на уровне портфеля, а не рекомендацию сделки.
/// </summary>
public static class PortfolioRiskPolicy
{
    public static RiskIncreasePolicyResult EvaluateRiskIncrease(
        PortfolioState state,
        PortfolioRiskPolicySettings settings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        var reasons = new List<ReasonCode>();
        if (!state.IsComplete)
            reasons.Add(ReasonCode.PortfolioDataIncomplete);
        if (!state.IsFresh)
            reasons.Add(ReasonCode.PortfolioDataStale);
        if (state.FreeCapitalPercent < settings.MinimumFreeCapitalPercent)
            reasons.Add(ReasonCode.InsufficientFreeCapital);
        if (state.GrossExposureToEquityPercent > settings.MaximumGrossExposureToEquityPercent)
            reasons.Add(ReasonCode.GrossExposureLimitExceeded);
        if (state.LargestPositionConcentrationPercent > settings.MaximumPositionConcentrationPercent)
            reasons.Add(ReasonCode.ConcentrationLimitExceeded);

        if (reasons.Count == 0)
            return RiskIncreasePolicyResult.Allowed();

        return RiskIncreasePolicyResult.Blocked(reasons);
    }
}
