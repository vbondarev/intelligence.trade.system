using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

/// <summary>Явный orchestration boundary между assessment, policy provider и aggregate.</summary>
public sealed class RecommendationService(
    IRecommendationPolicyDefinitionProvider policyDefinitionProvider,
    RecommendationPolicy policy)
{
    public async ValueTask<Recommendation> CreateAsync(
        PositionAssessment assessment,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var definition = await policyDefinitionProvider.GetAsync(cancellationToken);
        var evaluation = policy.Evaluate(assessment, definition, asOf);
        return Recommendation.Create(assessment, evaluation);
    }
}
