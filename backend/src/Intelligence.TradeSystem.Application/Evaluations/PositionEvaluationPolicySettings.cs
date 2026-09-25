using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Application.Evaluations;

/// <summary>
/// Неизменяемая production-конфигурация процесса оценки позиции.
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
