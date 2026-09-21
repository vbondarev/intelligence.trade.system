using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Recommendations;

/// <summary>Явный orchestration boundary между assessment, policy provider и aggregate.</summary>
public sealed class RecommendationService(
    IRecommendationPolicyDefinitionProvider policyDefinitionProvider,
    RecommendationPolicy policy,
    RecommendationStabilityPolicy stabilityPolicy,
    IRecommendationRepository recommendationRepository,
    IRecommendationStabilityStateRepository stabilityStateRepository,
    IRecommendationPublicationTransaction publicationTransaction,
    IPositionAssessmentRepository positionAssessmentRepository)
{
    private const int MaximumAttempts = 3;

    public async ValueTask<RecommendationApplicationResult> CreateAsync(
        UserId userId,
        PositionAssessment assessment,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var definition = await policyDefinitionProvider.GetAsync(cancellationToken);
        return await CreateAsync(
            userId,
            assessment,
            definition,
            asOf,
            cancellationToken);
    }

    public async ValueTask<RecommendationApplicationResult> CreateAsync(
        UserId userId,
        PositionAssessment assessment,
        PolicyDefinition policyDefinition,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(policyDefinition);
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));

        asOf = TimestampCanonicalizer.ToUtcMicroseconds(asOf);
        var persistedAssessment = await positionAssessmentRepository.GetByIdAsync(
            userId,
            assessment.Id,
            cancellationToken) ?? throw new InvalidOperationException(
                "Position assessment is unavailable in the requested user scope.");
        assessment = persistedAssessment;

        var evaluation = policy.Evaluate(assessment, policyDefinition, asOf);

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await recommendationRepository.GetCurrentForPositionAsync(
                userId,
                assessment.PositionId,
                cancellationToken);
            var pending = await stabilityStateRepository.GetAsync(
                userId,
                assessment.PositionId,
                cancellationToken);
            RecommendationCurrentExpectation currentExpectation = current is null
                ? new RecommendationCurrentExpectation.Absent()
                : new RecommendationCurrentExpectation.Present(
                    current.Value.Id,
                    current.Version);
            RecommendationStabilityStateExpectation pendingExpectation = pending is null
                ? new RecommendationStabilityStateExpectation.Absent()
                : new RecommendationStabilityStateExpectation.Present(
                    pending.Value.StateId,
                    pending.Value.BaselineRecommendationId,
                    pending.Version);
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
                policyDefinition.StabilityProfile,
                asOf);

            try
            {
                switch (stability.Kind)
                {
                    case RecommendationStabilityDecisionKind.KeepExisting:
                        await publicationTransaction.ConfirmKeepExistingAsync(
                            userId,
                            assessment.PositionId,
                            currentExpectation,
                            pendingExpectation,
                            cancellationToken);

                        return new(
                            RecommendationApplicationResultKind.KeptExisting,
                            current?.Value,
                            stability.Reason);

                    case RecommendationStabilityDecisionKind.PendingConfirmation:
                        if (current is null || stability.NextState is null)
                            throw new InvalidOperationException(
                                "Pending confirmation requires a current recommendation.");

                        var nextState = new RecommendationStabilityStateSnapshot(
                            pending is not null &&
                            pending.Value.BaselineRecommendationId == current.Value.Id
                                ? pending.Value.StateId
                                : Guid.NewGuid(),
                            current.Value.Id,
                            stability.NextState);
                        await publicationTransaction.SavePendingAsync(
                            userId,
                            assessment.PositionId,
                            currentExpectation,
                            nextState,
                            pendingExpectation,
                            cancellationToken);

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

                            await publicationTransaction.ReplaceAsync(
                                userId,
                                current.Value,
                                successor,
                                currentExpectation,
                                pendingExpectation,
                                cancellationToken);
                        }
                        else
                        {
                            await publicationTransaction.PublishInitialAsync(
                                userId,
                                successor,
                                currentExpectation,
                                pendingExpectation,
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
