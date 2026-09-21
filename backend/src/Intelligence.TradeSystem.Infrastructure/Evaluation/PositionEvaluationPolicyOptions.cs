using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Infrastructure.Evaluation;

public sealed class PositionEvaluationPolicyOptions
{
    public const string SectionName = "PositionEvaluationPolicy";

    public AssessmentRulesOptions? AssessmentRules { get; set; }
    public PortfolioRiskOptions? PortfolioRisk { get; set; }

    public PositionEvaluationPolicySettings ToDomain()
    {
        var assessment = AssessmentRules
            ?? throw new InvalidOperationException(
                "PositionEvaluationPolicy:AssessmentRules configuration is required.");
        var portfolio = PortfolioRisk
            ?? throw new InvalidOperationException(
                "PositionEvaluationPolicy:PortfolioRisk configuration is required.");

        if (string.IsNullOrWhiteSpace(assessment.Version))
            throw new InvalidOperationException(
                "PositionEvaluationPolicy:AssessmentRules:Version configuration is required.");
        if (!assessment.ValidityPeriod.HasValue)
            throw new InvalidOperationException(
                "PositionEvaluationPolicy:AssessmentRules:ValidityPeriod configuration is required.");
        if (!assessment.RsiOverbought.HasValue ||
            !assessment.RsiOversold.HasValue ||
            !assessment.NearbyLevelPercent.HasValue ||
            !assessment.LiquidationDangerPercent.HasValue ||
            !assessment.LowVolumeRatio.HasValue)
        {
            throw new InvalidOperationException(
                "All PositionEvaluationPolicy assessment rule values are required.");
        }

        if (!portfolio.MinimumFreeCapitalPercent.HasValue ||
            !portfolio.MaximumGrossExposureToEquityPercent.HasValue ||
            !portfolio.MaximumPositionConcentrationPercent.HasValue)
        {
            throw new InvalidOperationException(
                "All PositionEvaluationPolicy portfolio risk values are required.");
        }

        return new(
            new PositionAssessmentRules(
                new RuleVersion(assessment.Version),
                assessment.RsiOverbought.Value,
                assessment.RsiOversold.Value,
                assessment.NearbyLevelPercent.Value,
                assessment.LiquidationDangerPercent.Value,
                assessment.LowVolumeRatio.Value,
                assessment.ValidityPeriod.Value),
            new PortfolioRiskPolicySettings(
                portfolio.MinimumFreeCapitalPercent.Value,
                portfolio.MaximumGrossExposureToEquityPercent.Value,
                portfolio.MaximumPositionConcentrationPercent.Value));
    }

    public sealed class AssessmentRulesOptions
    {
        public string? Version { get; set; }
        public decimal? RsiOverbought { get; set; }
        public decimal? RsiOversold { get; set; }
        public decimal? NearbyLevelPercent { get; set; }
        public decimal? LiquidationDangerPercent { get; set; }
        public decimal? LowVolumeRatio { get; set; }
        public TimeSpan? ValidityPeriod { get; set; }
    }

    public sealed class PortfolioRiskOptions
    {
        public decimal? MinimumFreeCapitalPercent { get; set; }
        public decimal? MaximumGrossExposureToEquityPercent { get; set; }
        public decimal? MaximumPositionConcentrationPercent { get; set; }
    }
}
