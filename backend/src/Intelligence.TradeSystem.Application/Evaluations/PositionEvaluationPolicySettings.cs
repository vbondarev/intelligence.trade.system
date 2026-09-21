using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Evaluations;

/// <summary>
/// Immutable production configuration for the position evaluation workflow.
/// </summary>
public sealed record PositionEvaluationPolicySettings
{
    public PositionEvaluationPolicySettings(
        PositionAssessmentRules assessmentRules,
        PortfolioRiskPolicySettings portfolioRisk)
    {
        AssessmentRules = assessmentRules ?? throw new ArgumentNullException(nameof(assessmentRules));
        PortfolioRisk = portfolioRisk ?? throw new ArgumentNullException(nameof(portfolioRisk));
    }

    public PositionAssessmentRules AssessmentRules { get; }
    public PortfolioRiskPolicySettings PortfolioRisk { get; }
}
