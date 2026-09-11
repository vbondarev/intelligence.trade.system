using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Domain.Assessments;

/// <summary>
/// Вычисляет устойчивую identity всех конфигурационных входов оценки позиции.
/// </summary>
public static class PositionAssessmentConfigurationIdentity
{
    /// <summary>
    /// Добавляет к внешней policy identity assessment rules, portfolio risk settings и quality states.
    /// </summary>
    public static PolicyConfigurationIdentity Compose(
        PolicyConfigurationIdentity baseIdentity,
        PortfolioRiskPolicySettings portfolioRiskPolicySettings,
        PositionAssessmentRules rules,
        AssessmentDataQuality marketDataQuality,
        AssessmentDataQuality portfolioDataQuality)
    {
        ArgumentNullException.ThrowIfNull(portfolioRiskPolicySettings);
        ArgumentNullException.ThrowIfNull(rules);
        if (!Enum.IsDefined(marketDataQuality))
            throw new ArgumentOutOfRangeException(
                nameof(marketDataQuality), marketDataQuality, "Market data quality is not defined.");
        if (!Enum.IsDefined(portfolioDataQuality))
            throw new ArgumentOutOfRangeException(
                nameof(portfolioDataQuality), portfolioDataQuality, "Portfolio data quality is not defined.");

        var canonical = string.Join(
            "|",
            "position-assessment-input-v1",
            baseIdentity.Version,
            baseIdentity.Hash,
            rules.Version.Value,
            Format(rules.RsiOverboughtThreshold),
            Format(rules.RsiOversoldThreshold),
            Format(rules.NearbyLevelDistancePercent),
            Format(rules.LiquidationDangerDistancePercent),
            Format(rules.LowVolumeRatioThreshold),
            rules.ValidityPeriod.Ticks.ToString(CultureInfo.InvariantCulture),
            Format(portfolioRiskPolicySettings.MinimumFreeCapitalPercent),
            Format(portfolioRiskPolicySettings.MaximumGrossExposureToEquityPercent),
            Format(portfolioRiskPolicySettings.MaximumPositionConcentrationPercent),
            marketDataQuality,
            portfolioDataQuality);

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        return new PolicyConfigurationIdentity(baseIdentity.Version, $"sha256:{hash}");
    }

    private static string Format(decimal value) =>
        value.ToString("G29", CultureInfo.InvariantCulture);
}
