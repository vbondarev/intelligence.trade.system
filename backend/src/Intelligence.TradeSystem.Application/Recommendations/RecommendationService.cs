using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

/// <summary>Явный orchestration boundary между assessment, policy provider и aggregate.</summary>
public sealed class RecommendationService(
    IRecommendationPolicyDefinitionProvider policyDefinitionProvider,
    RecommendationPolicy policy,
    RecommendationStabilityPolicy stabilityPolicy,
    IRecommendationRepository? recommendationRepository = null,
    IRecommendationStabilityStateRepository? stabilityStateRepository = null,
    IRecommendationPublicationTransaction? publicationTransaction = null)
{
    private const int MaximumAttempts = 3;
    private IRecommendationRepository RecommendationRepository =>
        recommendationRepository ?? throw new InvalidOperationException(
            "Recommendation persistence is not configured.");
    private IRecommendationStabilityStateRepository StabilityStateRepository =>
        stabilityStateRepository ?? throw new InvalidOperationException(
            "Recommendation stability state persistence is not configured.");
    private IRecommendationPublicationTransaction PublicationTransaction =>
        publicationTransaction ?? throw new InvalidOperationException(
            "Recommendation publication persistence is not configured.");

    public async ValueTask<RecommendationApplicationResult> CreateAsync(
        UserId userId,
        PositionAssessment assessment,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        var definition = await policyDefinitionProvider.GetAsync(cancellationToken);
        var evaluation = policy.Evaluate(assessment, definition, asOf);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await RecommendationRepository.GetCurrentForPositionAsync(
                userId,
                assessment.PositionId,
                cancellationToken);
            var pending = await StabilityStateRepository.GetAsync(
                userId,
                assessment.PositionId,
                cancellationToken);
            var effectivePending = current is not null &&
                pending is not null &&
                pending.Value.BaselineRecommendationId == current.Value.Id
                ? pending.Value.State
                : null;
            if (attempt > 1 &&
                current is not null &&
                evaluation.CreatedAt <= current.Value.CreatedAt &&
                !RecommendationSemanticState.From(current.Value)
                    .Equals(RecommendationSemanticState.From(evaluation)))
            {
                throw new ConcurrencyConflictException(
                    "The candidate is no longer newer than the current recommendation after a concurrent publication.");
            }

            var stability = stabilityPolicy.Evaluate(
                current?.Value,
                evaluation,
                effectivePending,
                definition.StabilityProfile,
                asOf);

            try
            {
                switch (stability.Kind)
                {
                    case RecommendationStabilityDecisionKind.KeepExisting:
                        if (pending is not null)
                        {
                            await StabilityStateRepository.DeleteAsync(
                                userId,
                                assessment.PositionId,
                                pending.Version,
                                cancellationToken);
                        }

                        return new(
                            RecommendationApplicationResultKind.KeptExisting,
                            current?.Value,
                            stability.Reason);

                    case RecommendationStabilityDecisionKind.PendingConfirmation:
                        if (current is null || stability.NextState is null)
                            throw new InvalidOperationException(
                                "Pending confirmation requires a current recommendation.");

                        var nextState = new RecommendationStabilityStateSnapshot(
                            current.Value.Id,
                            stability.NextState);
                        if (pending is null ||
                            pending.Value.BaselineRecommendationId != nextState.BaselineRecommendationId ||
                            !pending.Value.State.Equals(nextState.State))
                        {
                            await StabilityStateRepository.SaveAsync(
                                userId,
                                assessment.PositionId,
                                nextState,
                                pending?.Version,
                                cancellationToken);
                        }

                        return new(
                            RecommendationApplicationResultKind.PendingConfirmation,
                            current.Value,
                            stability.Reason);

                    case RecommendationStabilityDecisionKind.PublishCandidate:
                        var successor = Recommendation.Create(assessment, evaluation);
                        if (current is not null &&
                            current.Value.Status is
                                RecommendationStatus.Active or RecommendationStatus.Acknowledged)
                        {
                            if (asOf >= current.Value.ValidUntil)
                                current.Value.ExpireIfDue(asOf);
                            else
                                current.Value.SupersedeBy(successor);

                            await PublicationTransaction.ReplaceAsync(
                                userId,
                                current.Value,
                                current.Version,
                                successor,
                                pending?.Version,
                                cancellationToken);
                        }
                        else
                        {
                            await PublicationTransaction.PublishInitialAsync(
                                userId,
                                successor,
                                pending?.Version,
                                cancellationToken);
                        }

                        return new(
                            RecommendationApplicationResultKind.Published,
                            successor,
                            stability.Reason);

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(assessment),
                            stability.Kind,
                            "Recommendation stability decision must be defined.");
                }
            }
            catch (ConcurrencyConflictException) when (attempt < MaximumAttempts)
            {
                // Re-read the current baseline and pending state before re-evaluating stability.
            }
        }

        throw new ConcurrencyConflictException(
            $"Recommendation evaluation for position {assessment.PositionId} exceeded the retry limit.");
    }
}
