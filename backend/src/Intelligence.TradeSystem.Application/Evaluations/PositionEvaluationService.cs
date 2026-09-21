using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Evaluations;

/// <summary>
/// Coordinates the explicit position evaluation workflow without private exchange synchronization.
/// </summary>
public sealed class PositionEvaluationService(
    IPositionRepository positionRepository,
    IExchangeAccountRepository exchangeAccountRepository,
    IPortfolioStateRepository portfolioStateRepository,
    IPositionAssessmentRepository positionAssessmentRepository,
    IRecommendationRepository recommendationRepository,
    IMarketSnapshotService marketSnapshotService,
    IRecommendationPolicyDefinitionProvider policyDefinitionProvider,
    PositionAssessmentService positionAssessmentService,
    RecommendationService recommendationService,
    PositionEvaluationPolicySettings policySettings,
    TimeProvider timeProvider)
{
    public async Task<PositionEvaluationReadResult> GetAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);

        var position = await positionRepository.GetByIdAsync(
            userId,
            positionId,
            cancellationToken);
        if (position is null)
            return PositionEvaluationReadResult.NotFound();

        var assessment = await positionAssessmentRepository.GetLatestForPositionAsync(
            userId,
            positionId,
            cancellationToken);
        if (assessment is null)
            return PositionEvaluationReadResult.NotEvaluated();

        var recommendation = await recommendationRepository.GetCurrentForPositionAsync(
            userId,
            positionId,
            cancellationToken);
        var currentRecommendation = recommendation is { } candidate &&
            candidate.Value.Status is RecommendationStatus.Active or RecommendationStatus.Acknowledged &&
            timeProvider.GetUtcNow() < candidate.Value.ValidUntil
            ? candidate.Value
            : null;

        return PositionEvaluationReadResult.Found(
            new PositionEvaluationSnapshot(assessment, currentRecommendation));
    }

    public async Task<PositionEvaluationResult> EvaluateAsync(
        UserId userId,
        PositionId positionId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        EnsurePositionId(positionId);

        var position = await positionRepository.GetByIdAsync(
            userId,
            positionId,
            cancellationToken);
        if (position is null)
            return PositionEvaluationResult.NotFound();

        if (position.Value.TrackingState == PositionTrackingState.Closed)
        {
            return PositionEvaluationResult.NotEvaluable(
                PositionEvaluationNotEvaluableReason.ClosedPosition);
        }

        var account = await exchangeAccountRepository.GetByIdAsync(
            userId,
            position.Value.ExchangePositionKey.ExchangeAccountId,
            cancellationToken);
        if (account is null)
        {
            return PositionEvaluationResult.NotEvaluable(
                PositionEvaluationNotEvaluableReason.PortfolioUnavailable);
        }

        var portfolio = await portfolioStateRepository.GetLatestAsync(
            userId,
            account.Value.Id,
            cancellationToken);
        if (portfolio is null)
        {
            return PositionEvaluationResult.NotEvaluable(
                PositionEvaluationNotEvaluableReason.PortfolioUnavailable);
        }

        if (!HasConsistentPortfolioPosition(position.Value, portfolio))
        {
            return PositionEvaluationResult.NotEvaluable(
                PositionEvaluationNotEvaluableReason.PortfolioInconsistent);
        }

        var policyDefinition = await policyDefinitionProvider.GetAsync(cancellationToken);
        var market = await marketSnapshotService.BuildSnapshotAsync(
            account.Value.ExchangeId,
            position.Value.ExchangePositionKey.InstrumentId.Value!,
            position.Value.MarketCategory,
            cancellationToken);

        var asOf = timeProvider.GetUtcNow();
        if (asOf < position.Value.LastObservedAt ||
            asOf < portfolio.CalculatedAt ||
            asOf < market.CapturedAtUtc)
        {
            return PositionEvaluationResult.NotEvaluable(
                PositionEvaluationNotEvaluableReason.TemporalInconsistency);
        }

        var portfolioQuality = ResolvePortfolioQuality(portfolio, asOf);
        var inputVersions = new PositionAssessmentInputVersions(
            position.Value.Id,
            account.Value.Id,
            position.Value.ExchangePositionKey.InstrumentId,
            position.Value.LastObservedAt,
            portfolio.CalculatedAt,
            market.CapturedAtUtc,
            policyDefinition.Identity);
        var input = new PositionAssessmentInput(
            position.Value,
            market,
            portfolio,
            policySettings.PortfolioRisk,
            inputVersions,
            AssessmentDataQuality.FreshCompleteReliable,
            portfolioQuality,
            asOf,
            policySettings.AssessmentRules,
            portfolio.IsFreshAt(asOf));
        var assessment = positionAssessmentService.Assess(input);

        await positionAssessmentRepository.SaveAsync(
            userId,
            assessment,
            cancellationToken);

        var persistedAssessment = await positionAssessmentRepository.GetByIdAsync(
            userId,
            assessment.Id,
            cancellationToken) ?? throw new InvalidOperationException(
                "The persisted position assessment could not be reloaded.");

        var recommendationResult = await recommendationService.CreateAsync(
            userId,
            persistedAssessment,
            policyDefinition,
            asOf,
            cancellationToken);

        return PositionEvaluationResult.Succeeded(
            new PositionEvaluationSnapshot(
                persistedAssessment,
                recommendationResult.Recommendation));
    }

    private static bool HasConsistentPortfolioPosition(
        Position position,
        PortfolioState portfolio)
    {
        if (portfolio.ExchangeAccountId != position.ExchangePositionKey.ExchangeAccountId)
            return false;

        var matchingPositions = portfolio.Positions
            .Where(candidate => candidate.PositionId == position.Id)
            .ToArray();
        if (matchingPositions.Length != 1)
            return false;

        var portfolioPosition = matchingPositions[0];
        return portfolioPosition.ExchangePositionKey == position.ExchangePositionKey &&
            portfolioPosition.MarketCategory == position.MarketCategory &&
            portfolioPosition.PositionSide == position.ExchangePositionKey.PositionSide;
    }

    private static AssessmentDataQuality ResolvePortfolioQuality(
        PortfolioState portfolio,
        DateTimeOffset asOf)
    {
        var quality = AssessmentDataQuality.FreshCompleteReliable;

        if (!portfolio.IsComplete || !portfolio.PositionsFullyReconciled)
            quality = MaxQuality(quality, AssessmentDataQuality.Partial);

        if (portfolio.Positions.Any(position =>
                position.TrackingState == PositionTrackingState.Stale) ||
            !portfolio.IsFreshAt(asOf))
        {
            quality = MaxQuality(quality, AssessmentDataQuality.Stale);
        }

        if (!portfolio.Capital.ObservedAt.HasValue ||
            portfolio.Positions.Any(position =>
                position.TrackingState == PositionTrackingState.Unknown))
        {
            quality = MaxQuality(quality, AssessmentDataQuality.Uncertain);
        }

        return quality;
    }

    private static AssessmentDataQuality MaxQuality(
        AssessmentDataQuality first,
        AssessmentDataQuality second) =>
        (AssessmentDataQuality)Math.Max((int)first, (int)second);

    private static void EnsureUserId(UserId userId)
    {
        if (userId == default)
            throw new ArgumentException("UserId must be initialized.", nameof(userId));
    }

    private static void EnsurePositionId(PositionId positionId)
    {
        if (positionId == default)
            throw new ArgumentException("PositionId must be initialized.", nameof(positionId));
    }
}
