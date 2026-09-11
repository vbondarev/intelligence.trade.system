namespace Intelligence.TradeSystem.Domain.Recommendations;

/// <summary>
/// Настраиваемые caps для разрешения увеличения позиции. Они могут только ужесточить
/// неотключаемые safety guards.
/// </summary>
public sealed record AddAllowedPolicyLimits
{
    public AddAllowedPolicyLimits(
        decimal maximumAdditionalPositionPercentOfEquity,
        decimal maximumAdditionalAvailableCapitalPercent,
        decimal minimumLiquidationDistancePercent)
    {
        ValidatePositive(maximumAdditionalPositionPercentOfEquity, nameof(maximumAdditionalPositionPercentOfEquity));
        ValidatePositive(maximumAdditionalAvailableCapitalPercent, nameof(maximumAdditionalAvailableCapitalPercent));
        ValidatePositive(minimumLiquidationDistancePercent, nameof(minimumLiquidationDistancePercent));

        MaximumAdditionalPositionPercentOfEquity = maximumAdditionalPositionPercentOfEquity;
        MaximumAdditionalAvailableCapitalPercent = maximumAdditionalAvailableCapitalPercent;
        MinimumLiquidationDistancePercent = minimumLiquidationDistancePercent;
    }

    public decimal MaximumAdditionalPositionPercentOfEquity { get; }
    public decimal MaximumAdditionalAvailableCapitalPercent { get; }
    public decimal MinimumLiquidationDistancePercent { get; }

    public static AddAllowedPolicyLimits Default => new(
        maximumAdditionalPositionPercentOfEquity: 10m,
        maximumAdditionalAvailableCapitalPercent: 25m,
        minimumLiquidationDistancePercent: 5m);

    private static void ValidatePositive(decimal value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        if (value == 0m)
            throw new ArgumentOutOfRangeException(parameterName, value, "AddAllowed limits must be positive.");
    }
}
