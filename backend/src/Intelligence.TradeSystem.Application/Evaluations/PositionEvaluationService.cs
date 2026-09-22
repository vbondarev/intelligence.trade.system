using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Application.Time;
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
    IPositionEvaluationTransaction evaluationTransaction,
    IApplicationEventOutbox applicationEventOutbox,
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

        var asOf = TimestampCanonicalizer.ToUtcMicroseconds(timeProvider.GetUtcNow());
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

        PositionAssessment? persistedAssessment = null;
        RecommendationApplicationResult? recommendationResult = null;
        await evaluationTransaction.ExecuteAsync(
            userId,
            positionId,
            async persistenceCancellationToken =>
            {
                var lockedPosition = await positionRepository.GetByIdAsync(
                    userId,
                    positionId,
                    persistenceCancellationToken);
                if (lockedPosition is null || lockedPosition.Version != position.Version)
                {
                    throw new ConcurrencyConflictException(
                        "The position evaluation snapshot became stale before persistence.");
                }

                var lockedAccount = await exchangeAccountRepository.GetByIdAsync(
                    userId,
                    account.Value.Id,
                    persistenceCancellationToken);
                if (lockedAccount is null ||
                    lockedAccount.Value.ConnectionStatus == ExchangeAccountConnectionStatus.Disabled ||
                    lockedAccount.Value.ExchangeId != account.Value.ExchangeId ||
                    lockedAccount.Value.ProviderIdentity != account.Value.ProviderIdentity)
                {
                    throw new ConcurrencyConflictException(
                        "The exchange account evaluation snapshot became stale before persistence.");
                }

                var lockedPortfolio = await portfolioStateRepository.GetLatestAsync(
                    userId,
                    account.Value.Id,
                    persistenceCancellationToken);
                if (lockedPortfolio is null ||
                    !HasSameEvaluationInputs(portfolio, lockedPortfolio) ||
                    !HasConsistentPortfolioPosition(lockedPosition.Value, lockedPortfolio))
                {
                    throw new ConcurrencyConflictException(
                        "The portfolio evaluation snapshot became stale before persistence.");
                }

                await positionAssessmentRepository.SaveAsync(
                    userId,
                    assessment,
                    persistenceCancellationToken);

                persistedAssessment = await positionAssessmentRepository.GetByIdAsync(
                    userId,
                    assessment.Id,
                    persistenceCancellationToken) ?? throw new InvalidOperationException(
                    "The persisted position assessment could not be reloaded.");

                recommendationResult = await recommendationService.CreateAsync(
                    userId,
                    persistedAssessment,
                    policyDefinition,
                    asOf,
                    persistenceCancellationToken);

                await applicationEventOutbox.AddAsync(
                    new PositionEvaluationUpdatedEventV1(
                        Guid.NewGuid(),
                        asOf,
                        userId.Value,
                        positionId.Value),
                    persistenceCancellationToken);
            },
            cancellationToken);

        if (persistedAssessment is null || recommendationResult is null)
        {
            throw new InvalidOperationException(
                "The evaluation persistence boundary completed without an evaluation result.");
        }

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
            portfolioPosition.PositionSide == position.ExchangePositionKey.PositionSide &&
            portfolioPosition.TrackingState == position.TrackingState &&
            portfolioPosition.Size == position.Size &&
            portfolioPosition.PositionValue == position.PositionValue &&
            portfolioPosition.UnrealizedPnl == position.UnrealizedPnl &&
            portfolioPosition.AverageEntryPrice == position.AverageEntryPrice &&
            portfolioPosition.MarkPrice == position.MarkPrice &&
            portfolioPosition.LiquidationPrice == position.LiquidationPrice &&
            portfolioPosition.Leverage == position.Leverage &&
            portfolioPosition.LastObservedAt == position.LastObservedAt;
    }

    private static bool HasSameEvaluationInputs(
        PortfolioState expected,
        PortfolioState actual) =>
        expected.ExchangeAccountId == actual.ExchangeAccountId &&
        expected.CalculatedAt == actual.CalculatedAt &&
        expected.StaleAfter == actual.StaleAfter &&
        expected.PositionsFullyReconciled == actual.PositionsFullyReconciled &&
        expected.IsComplete == actual.IsComplete &&
        expected.IsFresh == actual.IsFresh &&
        expected.Capital == actual.Capital &&
        expected.Positions
            .OrderBy(position => position.PositionId.Value)
            .SequenceEqual(actual.Positions.OrderBy(position => position.PositionId.Value));

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
