using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

/// <summary>Application-port для получения уже валидированной внешней policy definition.</summary>
public interface IRecommendationPolicyDefinitionProvider
{
    ValueTask<PolicyDefinition> GetAsync(CancellationToken cancellationToken = default);
}
