using Intelligence.TradeSystem.Application.Accounts;
using Intelligence.TradeSystem.Application.Assessments;
using Intelligence.TradeSystem.Application.Concurrency;
using Intelligence.TradeSystem.Application.Events;
using Intelligence.TradeSystem.Application.Evaluations;
using Intelligence.TradeSystem.Application.Portfolio;
using Intelligence.TradeSystem.Application.Recommendations;
using Intelligence.TradeSystem.Application.Time;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Application.Tests.Evaluations;

public sealed class PositionEvaluationServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_missing_position_returns_not_found_without_reading_evaluation_dependencies()
    {
        var harness = CreateHarness();
        harness.Positions.Value = null;

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.NotFound, result.Outcome);
        Assert.Null(harness.Assessments.Latest);
        Assert.Empty(harness.Assessments.Calls);
        Assert.Empty(harness.Recommendations.Calls);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
        Assert.Empty(harness.Publication.Calls);
    }

    [Fact]
    public async Task Get_owned_position_without_assessment_returns_not_evaluated()
    {
        var harness = CreateHarness();

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.NotEvaluated, result.Outcome);
        Assert.Empty(harness.Recommendations.Calls);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
    }

    [Fact]
    public async Task Get_returns_latest_assessment_and_same_assessment_recommendation()
    {
        var harness = CreateHarness();
        var assessment = CreateLegacyAssessment(harness.Position, T0.AddMinutes(1));
        harness.Assessments.Latest = assessment;
        var recommendation = CreateLegacyRecommendation(
            assessment,
            T0.AddMinutes(2),
            T0.AddMinutes(10));
        harness.Recommendations.Current = new Versioned<Recommendation>(
            recommendation,
            ConcurrencyVersion.Initial);

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.Found, result.Outcome);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(assessment.Id, result.Snapshot!.Assessment.Id);
        Assert.Equal(recommendation.Id, result.Snapshot.Recommendation!.Id);
        Assert.Equal(assessment.Id, result.Snapshot.Recommendation.AssessmentId);
    }

    [Fact]
    public async Task Get_allows_current_recommendation_to_reference_previous_assessment()
    {
        var harness = CreateHarness();
        var assessmentA1 = CreateLegacyAssessment(harness.Position, T0.AddMinutes(1));
        var assessmentA2 = CreateLegacyAssessment(harness.Position, T0.AddMinutes(2));
        harness.Assessments.Latest = assessmentA2;
        var recommendationR1 = CreateLegacyRecommendation(
            assessmentA1,
            T0.AddMinutes(3),
            T0.AddMinutes(10));
        harness.Recommendations.Current = new Versioned<Recommendation>(
            recommendationR1,
            ConcurrencyVersion.Initial);

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.Found, result.Outcome);
        Assert.Equal(assessmentA2.Id, result.Snapshot!.Assessment.Id);
        Assert.Equal(recommendationR1.Id, result.Snapshot.Recommendation!.Id);
        Assert.Equal(assessmentA1.Id, result.Snapshot.Recommendation.AssessmentId);
        Assert.NotEqual(result.Snapshot.Assessment.Id, result.Snapshot.Recommendation.AssessmentId);
    }

    [Fact]
    public async Task Get_returns_null_recommendation_when_no_current_recommendation_exists()
    {
        var harness = CreateHarness();
        harness.Assessments.Latest = CreateLegacyAssessment(harness.Position, T0.AddMinutes(1));

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.Found, result.Outcome);
        Assert.Null(result.Snapshot!.Recommendation);
    }

    [Fact]
    public async Task Get_filters_expired_recommendation_without_lifecycle_mutation()
    {
        var harness = CreateHarness();
        harness.Assessments.Latest = CreateLegacyAssessment(harness.Position, T0.AddMinutes(1));
        var expired = CreateLegacyRecommendation(
            harness.Assessments.Latest,
            T0.AddMinutes(1),
            T0.AddMinutes(2));
        harness.Recommendations.Current = new Versioned<Recommendation>(
            expired,
            ConcurrencyVersion.Initial);

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.Found, result.Outcome);
        Assert.Null(result.Snapshot!.Recommendation);
        Assert.Equal(RecommendationStatus.Active, expired.Status);
        Assert.Empty(harness.Recommendations.Writes);
        Assert.Empty(harness.Publication.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
    }

    [Fact]
    public async Task Get_reads_evaluation_for_closed_position()
    {
        var harness = CreateHarness();
        harness.Position.Close(T0.AddMinutes(1));
        harness.Assessments.Latest = CreateLegacyAssessment(harness.Position, T0.AddMinutes(2));

        var result = await harness.Service.GetAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationReadOutcome.Found, result.Outcome);
        Assert.Equal(harness.Assessments.Latest.Id, result.Snapshot!.Assessment.Id);
    }

    [Fact]
    public async Task Evaluate_missing_position_returns_not_found_before_market_policy_or_writes()
    {
        var harness = CreateHarness();
        harness.Positions.Value = null;

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationOutcome.NotFound, result.Outcome);
        Assert.Empty(harness.Accounts.Calls);
        Assert.Empty(harness.Portfolios.Calls);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
        Assert.Empty(harness.Recommendations.Writes);
    }

    [Fact]
    public async Task Evaluate_closed_position_stops_before_market_io()
    {
        var harness = CreateHarness();
        harness.Position.Close(T0.AddMinutes(1));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.ClosedPosition,
            result.NotEvaluableReason);
        Assert.Empty(harness.Accounts.Calls);
        Assert.Empty(harness.Portfolios.Calls);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
    }

    [Fact]
    public async Task Evaluate_missing_portfolio_stops_before_market_io()
    {
        var harness = CreateHarness();
        harness.Portfolios.Value = null;

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.PortfolioUnavailable,
            result.NotEvaluableReason);
        Assert.Single(harness.Accounts.Calls);
        Assert.Single(harness.Portfolios.Calls);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
    }

    [Fact]
    public async Task Evaluate_inconsistent_portfolio_stops_before_market_io()
    {
        var harness = CreateHarness();
        harness.Portfolios.Value = PortfolioState.Create(
            harness.Account.Id,
            [],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.PortfolioInconsistent,
            result.NotEvaluableReason);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
    }

    [Fact]
    public async Task Evaluate_rejects_portfolio_snapshot_from_a_newer_position_observation()
    {
        var harness = CreateHarness();
        var portfolioPosition = CreatePortfolioPositionState(
            harness.Position,
            lastObservedAt: T0.AddMinutes(1));
        harness.Portfolios.Value = PortfolioState.Restore(
            harness.Account.Id,
            [portfolioPosition],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(2),
            TimeSpan.FromMinutes(5));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.PortfolioInconsistent,
            result.NotEvaluableReason);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
        Assert.Empty(harness.Publication.Calls);
    }

    [Fact]
    public async Task Evaluate_rejects_different_position_facts_with_the_same_observation_timestamp()
    {
        var harness = CreateHarness();
        var portfolioPosition = CreatePortfolioPositionState(
            harness.Position,
            markPrice: harness.Position.MarkPrice!.Value + 1m);
        harness.Portfolios.Value = PortfolioState.Restore(
            harness.Account.Id,
            [portfolioPosition],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.PortfolioInconsistent,
            result.NotEvaluableReason);
        Assert.Empty(harness.Market.Calls);
        Assert.Empty(harness.Policy.Calls);
        Assert.Equal(0, harness.Assessments.SaveCalls);
        Assert.Empty(harness.Publication.Calls);
    }

    [Fact]
    public async Task Evaluate_uses_persisted_exchange_symbol_and_category_for_market_request()
    {
        var harness = CreateHarness();

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationOutcome.Succeeded, result.Outcome);
        var request = Assert.Single(harness.Market.Calls);
        Assert.Equal(harness.Account.ExchangeId, request.ExchangeId);
        Assert.Equal(harness.Position.ExchangePositionKey.InstrumentId.Value, request.Symbol);
        Assert.Equal(harness.Position.MarketCategory, request.Category);
    }

    [Fact]
    public async Task Evaluate_saves_assessment_before_running_recommendation_workflow()
    {
        var harness = CreateHarness();

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationOutcome.Succeeded, result.Outcome);
        var saveIndex = harness.Order.IndexOf("assessment-save");
        var recommendationIndex = harness.Order.IndexOf("recommendation-read");
        Assert.True(saveIndex >= 0);
        Assert.True(recommendationIndex > saveIndex);
        Assert.Single(harness.Policy.Calls);
        Assert.Equal(harness.Policy.Definition.Identity, result.Snapshot!.Assessment.InputVersions.BasePolicyConfigurationIdentity);
        Assert.Equal(harness.Policy.Definition.Identity, result.Snapshot.Recommendation!.PolicyIdentity);
        var evaluationEvent = Assert.IsType<PositionEvaluationUpdatedEventV1>(
            Assert.Single(harness.EvaluationOutbox.Events));
        Assert.Equal(harness.UserId.Value, evaluationEvent.UserId);
        Assert.Equal(harness.Position.Id.Value, evaluationEvent.PositionId);
    }

    [Fact]
    public async Task Evaluate_fresh_complete_portfolio_preserves_allowed_quality()
    {
        var harness = CreateHarness();

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        var assessment = result.Snapshot!.Assessment;
        Assert.Equal(AssessmentDataQuality.FreshCompleteReliable, assessment.Result.DataQuality.Portfolio);
        Assert.Equal(AssessmentDataQuality.FreshCompleteReliable, assessment.Result.DataQuality.Overall);
        Assert.Equal(RiskIncreaseDecision.Allowed, assessment.PortfolioRiskDecision);
    }

    [Fact]
    public async Task Evaluate_marks_portfolio_stale_at_evaluation_time_even_when_snapshot_was_fresh()
    {
        var harness = CreateHarness(asOf: T0.AddMinutes(3));
        harness.Portfolios.Value = PortfolioState.Create(
            harness.Account.Id,
            [harness.Position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(2));

        Assert.True(harness.Portfolios.Value.IsFresh);
        Assert.False(harness.Portfolios.Value.IsFreshAt(harness.AsOf));
        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        var assessment = result.Snapshot!.Assessment;
        Assert.Equal(AssessmentDataQuality.Stale, assessment.Result.DataQuality.Portfolio);
        Assert.Equal(RiskIncreaseDecision.Blocked, assessment.PortfolioRiskDecision);
    }

    [Fact]
    public async Task Evaluate_marks_reconciliation_degradation_partial_and_blocks_risk_increase()
    {
        var harness = CreateHarness();
        harness.Portfolios.Value = PortfolioState.Create(
            harness.Account.Id,
            [harness.Position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            positionsFullyReconciled: false);

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            AssessmentDataQuality.Partial,
            result.Snapshot!.Assessment.Result.DataQuality.Portfolio);
        Assert.Equal(
            RiskIncreaseDecision.Blocked,
            result.Snapshot.Assessment.PortfolioRiskDecision);
    }

    [Fact]
    public async Task Evaluate_marks_unknown_tracking_state_uncertain_and_blocks_risk_increase()
    {
        var harness = CreateHarness();
        harness.Position.MarkUnknown(T0.AddMinutes(1));
        harness.Positions.Value = new Versioned<Position>(
            harness.Position,
            ConcurrencyVersion.Initial);
        harness.Portfolios.Value = PortfolioState.Create(
            harness.Account.Id,
            [harness.Position],
            new PortfolioCapitalState(1_000m, 800m, T0.AddMinutes(1), 1_000m),
            T0.AddMinutes(2),
            TimeSpan.FromMinutes(5));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            AssessmentDataQuality.Uncertain,
            result.Snapshot!.Assessment.Result.DataQuality.Portfolio);
        Assert.Equal(
            RiskIncreaseDecision.Blocked,
            result.Snapshot.Assessment.PortfolioRiskDecision);
    }

    [Fact]
    public async Task Evaluate_uses_existing_assessment_service_market_diagnostics_downgrade()
    {
        var harness = CreateHarness();
        harness.Market.Snapshot = CreateMarketSnapshot(
            T0.AddMinutes(2),
            new IndicatorDiagnosticSnapshot
            {
                Timeframe = "4h",
                Indicator = "rsi14",
                Reason = "PartialWindow",
                IsFallback = true,
                Message = "fallback",
            });

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        var assessment = result.Snapshot!.Assessment;
        Assert.Equal(AssessmentDataQuality.Partial, assessment.Result.DataQuality.Market);
        Assert.Equal(RiskIncreaseDecision.Blocked, assessment.PortfolioRiskDecision);
    }

    [Fact]
    public async Task Evaluate_does_not_save_when_market_is_unavailable()
    {
        var harness = CreateHarness();
        harness.Market.Exception = new MarketDataUnavailableException("market unavailable");

        await Assert.ThrowsAsync<MarketDataUnavailableException>(
            () => harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id));

        Assert.Equal(0, harness.Assessments.SaveCalls);
        Assert.Empty(harness.Recommendations.Writes);
        Assert.Empty(harness.Publication.Calls);
    }

    [Fact]
    public async Task Evaluate_returns_temporal_inconsistency_without_persisting_assessment()
    {
        var harness = CreateHarness(asOf: T0.AddMinutes(-1));

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            PositionEvaluationNotEvaluableReason.TemporalInconsistency,
            result.NotEvaluableReason);
        Assert.Equal(0, harness.Assessments.SaveCalls);
    }

    [Fact]
    public async Task Evaluate_returns_published_recommendation()
    {
        var harness = CreateHarness();

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(PositionEvaluationOutcome.Succeeded, result.Outcome);
        Assert.Equal(RecommendationApplicationResultKind.Published, harness.LastRecommendationKind);
        Assert.Equal(1, harness.Publication.PublishInitialCalls);
        Assert.NotNull(result.Snapshot!.Recommendation);
    }

    [Fact]
    public async Task Evaluate_uses_the_same_canonical_as_of_for_assessment_and_recommendation()
    {
        var asOf = new DateTimeOffset(
            T0.AddMinutes(3).Ticks + 1,
            TimeSpan.Zero);
        var harness = CreateHarness(asOf);

        var result = await harness.Service.EvaluateAsync(
            harness.UserId,
            harness.Position.Id);

        Assert.Equal(PositionEvaluationOutcome.Succeeded, result.Outcome);
        Assert.Equal(
            TimestampCanonicalizer.ToUtcMicroseconds(asOf),
            result.Snapshot!.Assessment.CreatedAt);
        Assert.NotNull(result.Snapshot.Recommendation);
    }

    [Fact]
    public async Task Evaluate_propagates_recommendation_concurrency_conflict_after_assessment_persistence()
    {
        var harness = CreateHarness();
        harness.Publication.ThrowConcurrencyConflicts = true;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id));

        Assert.Equal(1, harness.Assessments.SaveCalls);
        Assert.Equal(3, harness.Publication.PublishInitialCalls);
        Assert.Empty(harness.EvaluationOutbox.Events);
    }

    [Fact]
    public async Task Evaluate_returns_new_assessment_with_kept_previous_recommendation()
    {
        var harness = CreateHarness();
        var previousAssessment = CreateStructuredAssessment(
            harness,
            T0.AddMinutes(2));
        var current = Recommendation.Create(
            previousAssessment,
            new RecommendationPolicy().Evaluate(
                previousAssessment,
                harness.Policy.Definition,
                T0.AddMinutes(2)));
        harness.Recommendations.Current = new Versioned<Recommendation>(
            current,
            ConcurrencyVersion.Initial);

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(RecommendationApplicationResultKind.KeptExisting, harness.LastRecommendationKind);
        Assert.Equal(previousAssessment.Id, result.Snapshot!.Recommendation!.AssessmentId);
        Assert.NotEqual(result.Snapshot.Assessment.Id, result.Snapshot.Recommendation.AssessmentId);
        Assert.Equal(1, harness.Publication.ConfirmKeepCalls);
        Assert.Single(harness.EvaluationOutbox.Events);
    }

    [Fact]
    public async Task Evaluate_returns_current_previous_recommendation_while_pending_confirmation()
    {
        var harness = CreateHarness();
        var previousAssessment = CreateLegacyAssessment(harness.Position, T0.AddMinutes(1));
        var current = CreateLegacyRecommendation(
            previousAssessment,
            T0.AddMinutes(2),
            T0.AddMinutes(10));
        harness.Recommendations.Current = new Versioned<Recommendation>(
            current,
            ConcurrencyVersion.Initial);

        var result = await harness.Service.EvaluateAsync(harness.UserId, harness.Position.Id);

        Assert.Equal(
            RecommendationApplicationResultKind.PendingConfirmation,
            harness.LastRecommendationKind);
        Assert.Equal(current.Id, result.Snapshot!.Recommendation!.Id);
        Assert.Equal(previousAssessment.Id, result.Snapshot.Recommendation.AssessmentId);
        Assert.Equal(1, harness.Publication.SavePendingCalls);
        Assert.Single(harness.EvaluationOutbox.Events);
    }

    [Fact]
    public async Task Evaluate_forwards_the_same_cancellation_token_through_workflow()
    {
        var harness = CreateHarness();
        using var cancellation = new CancellationTokenSource();

        await harness.Service.EvaluateAsync(
            harness.UserId,
            harness.Position.Id,
            cancellation.Token);

        Assert.All(
            harness.AllCancellationTokens,
            token => Assert.Equal(cancellation.Token, token));
    }

    private static TestHarness CreateHarness(DateTimeOffset? asOf = null)
    {
        var userId = UserId.New();
        var account = CreateAccount(userId);
        var position = CreatePosition(account.Id);
        var market = CreateMarketSnapshot(T0.AddMinutes(2));
        var resolvedPortfolio = PortfolioState.Create(
            account.Id,
            [position],
            new PortfolioCapitalState(1_000m, 800m, T0, 1_000m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));
        var policy = PolicyDefinition.Default;
        var settings = new PositionEvaluationPolicySettings(
            PositionAssessmentRules.Default,
            new PortfolioRiskPolicySettings(0m, 200m, 100m));
        var harness = new TestHarness(
            userId,
            account,
            position,
            resolvedPortfolio,
            market,
            policy,
            settings,
            asOf ?? T0.AddMinutes(3));
        harness.Portfolios.Value = resolvedPortfolio;
        return harness;
    }

    private static PositionAssessment CreateStructuredAssessment(
        TestHarness harness,
        DateTimeOffset asOf)
    {
        var input = new PositionAssessmentInput(
            harness.Position,
            harness.Market.Snapshot,
            harness.Portfolios.Value!,
            harness.Settings.PortfolioRisk,
            new PositionAssessmentInputVersions(
                harness.Position.Id,
                harness.Account.Id,
                harness.Position.ExchangePositionKey.InstrumentId,
                harness.Position.LastObservedAt,
                harness.Portfolios.Value!.CalculatedAt,
                harness.Market.Snapshot.CapturedAtUtc,
                harness.Policy.Definition.Identity),
            AssessmentDataQuality.FreshCompleteReliable,
            AssessmentDataQuality.FreshCompleteReliable,
            asOf,
            harness.Settings.AssessmentRules,
            harness.Portfolios.Value!.IsFreshAt(asOf));
        return new PositionAssessmentService().Assess(input);
    }

    private static PositionAssessment CreateLegacyAssessment(
        Position position,
        DateTimeOffset createdAt) =>
        PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                position.Id,
                position.ExchangePositionKey.ExchangeAccountId,
                position.ExchangePositionKey.InstrumentId,
                createdAt.AddMinutes(-2),
                createdAt.AddMinutes(-1),
                createdAt),
            new RuleVersion("assessment-v1"),
            RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            [],
            createdAt,
            createdAt.AddMinutes(10));

    private static PortfolioPositionState CreatePortfolioPositionState(
        Position position,
        DateTimeOffset? lastObservedAt = null,
        decimal? markPrice = null) =>
        new(
            position.Id,
            position.ExchangePositionKey,
            position.MarketCategory,
            position.ExchangePositionKey.PositionSide,
            position.TrackingState,
            position.Size,
            position.PositionValue,
            position.UnrealizedPnl,
            position.AverageEntryPrice,
            markPrice ?? position.MarkPrice,
            position.LiquidationPrice,
            position.Leverage,
            lastObservedAt ?? position.LastObservedAt);

    private static Recommendation CreateLegacyRecommendation(
        PositionAssessment assessment,
        DateTimeOffset createdAt,
        DateTimeOffset validUntil) =>
        Recommendation.Create(
            assessment,
            PositionAction.Watch,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-v1"),
            [],
            createdAt,
            validUntil);

    private static ExchangeAccount CreateAccount(UserId userId) =>
        ExchangeAccount.Create(
            ExchangeAccountId.New(),
            userId,
            ExchangeId.Bybit,
            ExchangeAccountProviderIdentity.From("provider-account"),
            ExchangeAccountConnectionStatus.Connected,
            ExchangeAccountCapabilities.ReadBalance | ExchangeAccountCapabilities.ReadPositions);

    private static Position CreatePosition(ExchangeAccountId accountId) =>
        Position.Create(
            ExchangePositionKey.Create(
                accountId,
                InstrumentId.From("BTCUSDT"),
                PositionSide.Long,
                0),
            MarketCategory.Linear,
            1m,
            T0,
            T0,
            averageEntryPrice: 100m,
            positionValue: 100m,
            leverage: 2m,
            markPrice: 105m,
            breakEvenPrice: 100m,
            liquidationPrice: 50m,
            unrealizedPnl: 1m,
            stopLoss: 95m);

    private static MarketSnapshot CreateMarketSnapshot(
        DateTimeOffset capturedAt,
        params IndicatorDiagnosticSnapshot[] diagnostics)
    {
        var timeframe = new TimeframeAnalysisSnapshot
        {
            Timeframe = "4h",
            LastCandleOpenTimeUtc = capturedAt.AddMinutes(-1),
            LastCandle = new CandleSnapshot
            {
                OpenTimeUtc = capturedAt.AddMinutes(-1),
                Open = 105m,
                High = 107m,
                Low = 103m,
                Close = 105m,
                Volume = 100m,
                Turnover = 10_500m,
            },
            Rsi14 = 50m,
            Rsi14IsReliable = true,
            Atr14 = 2m,
            AtrIsReliable = true,
            VolumeRatio = 1m,
            VolumeRatioIsReliable = true,
            Trend = MarketTrend.Bullish,
            TrendStrengthScore = 0.8m,
            Support1 = 100m,
            Support1Strength = 0.7m,
            Resistance1 = 110m,
            Resistance1Strength = 0.7m,
        };

        return new MarketSnapshot
        {
            Exchange = "Bybit",
            Symbol = "BTCUSDT",
            Category = "Linear",
            CapturedAtUtc = capturedAt,
            Price = new PriceSnapshot { LastPrice = 105m, MarkPrice = 105m },
            Derivatives = new(),
            OrderBook = new() { CapturedAtUtc = capturedAt },
            TradeFlow = new() { WindowEndUtc = capturedAt },
            M15 = timeframe with { Timeframe = "15m" },
            H1 = timeframe with { Timeframe = "1h" },
            H4 = timeframe,
            D1 = timeframe with { Timeframe = "1d" },
            Sentiment = new(),
            IndicatorDiagnostics = diagnostics,
        };
    }

    private sealed class TestHarness
    {
        public TestHarness(
            UserId userId,
            ExchangeAccount account,
            Position position,
            PortfolioState portfolio,
            MarketSnapshot market,
            PolicyDefinition policy,
            PositionEvaluationPolicySettings settings,
            DateTimeOffset asOf)
        {
            UserId = userId;
            Account = account;
            Position = position;
            AsOf = asOf;
            Settings = settings;
            Policy = new RecordingPolicyProvider(policy);
            Positions = new RecordingPositionRepository
            {
                Value = new Versioned<Position>(position, ConcurrencyVersion.Initial),
            };
            Accounts = new RecordingExchangeAccountRepository
            {
                Value = new Versioned<ExchangeAccount>(account, ConcurrencyVersion.Initial),
            };
            Portfolios = new RecordingPortfolioStateRepository { Value = portfolio };
            Assessments = new RecordingAssessmentRepository(Order);
            Recommendations = new RecordingRecommendationRepository(Order);
            Market = new RecordingMarketSnapshotService(market);
            Stability = new RecordingStabilityStateRepository();
            Publication = new RecordingPublicationTransaction(Order);
            EvaluationTransaction = new RecordingEvaluationTransaction();
            EvaluationOutbox = new RecordingEvaluationEventOutbox();
            RecommendationService = new(
                Policy,
                new RecommendationPolicy(),
                new RecommendationStabilityPolicy(),
                Recommendations,
                Stability,
                Publication,
                Assessments);
            Service = new(
                Positions,
                Accounts,
                Portfolios,
                Assessments,
                Recommendations,
                Market,
                Policy,
                new PositionAssessmentService(),
                RecommendationService,
                EvaluationTransaction,
                EvaluationOutbox,
                settings,
                new FixedTimeProvider(asOf));
        }

        public UserId UserId { get; }
        public ExchangeAccount Account { get; }
        public Position Position { get; }
        public DateTimeOffset AsOf { get; }
        public PositionEvaluationPolicySettings Settings { get; }
        public List<string> Order { get; } = [];
        public RecordingPolicyProvider Policy { get; }
        public RecordingPositionRepository Positions { get; }
        public RecordingExchangeAccountRepository Accounts { get; }
        public RecordingPortfolioStateRepository Portfolios { get; }
        public RecordingAssessmentRepository Assessments { get; }
        public RecordingRecommendationRepository Recommendations { get; }
        public RecordingMarketSnapshotService Market { get; }
        public RecordingStabilityStateRepository Stability { get; }
        public RecordingPublicationTransaction Publication { get; }
        public RecordingEvaluationTransaction EvaluationTransaction { get; }
        public RecordingEvaluationEventOutbox EvaluationOutbox { get; }
        public RecommendationService RecommendationService { get; }
        public PositionEvaluationService Service { get; }
        public RecommendationApplicationResultKind? LastRecommendationKind =>
            Publication.LastKind;
        public PolicyDefinition PolicyDefinition => Policy.Definition;
        public IEnumerable<CancellationToken> AllCancellationTokens =>
            Positions.CancellationTokens
                .Concat(Accounts.CancellationTokens)
                .Concat(Portfolios.CancellationTokens)
                .Concat(Assessments.CancellationTokens)
                .Concat(Recommendations.CancellationTokens)
                .Concat(Market.CancellationTokens)
                .Concat(Policy.CancellationTokens)
                .Concat(Publication.CancellationTokens)
                .Concat(Stability.CancellationTokens);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingPositionRepository : IPositionRepository
    {
        public Versioned<Position>? Value { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<Versioned<Position>?> GetByIdAsync(
            UserId userId,
            PositionId id,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Value is { Value.Id: var value } && value == id ? Value : null);
        }

        public Task<IReadOnlyCollection<Versioned<Position>>> GetByExchangeAccountAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            Position position,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingExchangeAccountRepository : IExchangeAccountRepository
    {
        public Versioned<ExchangeAccount>? Value { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<(UserId UserId, ExchangeAccountId Id)> Calls { get; } = [];

        public Task<Versioned<ExchangeAccount>?> GetByIdAsync(
            UserId userId,
            ExchangeAccountId id,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((userId, id));
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Value is { Value.Id: var value } && value == id ? Value : null);
        }

        public Task<IReadOnlyList<Versioned<ExchangeAccount>>> ListActiveAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            ExchangeAccount account,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            UserId userId,
            ExchangeAccountId id,
            ConcurrencyVersion expectedVersion,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingPortfolioStateRepository : IPortfolioStateRepository
    {
        public PortfolioState? Value { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<(UserId UserId, ExchangeAccountId Id)> Calls { get; } = [];

        public Task<PortfolioState?> GetLatestAsync(
            UserId userId,
            ExchangeAccountId exchangeAccountId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((userId, exchangeAccountId));
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Value);
        }

        public Task SaveAsync(
            UserId userId,
            PortfolioState state,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAssessmentRepository(List<string> order)
        : IPositionAssessmentRepository
    {
        public PositionAssessment? Latest { get; set; }
        public PositionAssessment? Persisted { get; private set; }
        public int SaveCalls { get; private set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<string> Calls { get; } = [];

        public Task<PositionAssessment?> GetLatestForPositionAsync(
            UserId userId,
            PositionId positionId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(GetLatestForPositionAsync));
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Latest);
        }

        public Task<PositionAssessment?> GetByIdAsync(
            UserId userId,
            PositionAssessmentId id,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(GetByIdAsync));
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult<PositionAssessment?>(Persisted?.Id == id ? Persisted : null);
        }

        public Task SaveAsync(
            UserId userId,
            PositionAssessment assessment,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(SaveAsync));
            order.Add("assessment-save");
            CancellationTokens.Add(cancellationToken);
            SaveCalls++;
            Persisted = assessment;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRecommendationRepository(List<string> order)
        : IRecommendationRepository
    {
        public Versioned<Recommendation>? Current { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<string> Calls { get; } = [];
        public List<string> Writes { get; } = [];

        public Task<Versioned<Recommendation>?> GetCurrentForPositionAsync(
            UserId userId,
            PositionId positionId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(GetCurrentForPositionAsync));
            order.Add("recommendation-read");
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Current);
        }

        public Task<Versioned<Recommendation>?> GetByIdAsync(
            UserId userId,
            RecommendationId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Versioned<Recommendation>?>(null);

        public Task EnsureCurrentAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            Recommendation recommendation,
            ConcurrencyVersion? expectedVersion,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(nameof(SaveAsync));
            return Task.FromResult(ConcurrencyVersion.Initial);
        }
    }

    private sealed class RecordingMarketSnapshotService(MarketSnapshot snapshot)
        : IMarketSnapshotService
    {
        public MarketSnapshot Snapshot { get; set; } = snapshot;
        public Exception? Exception { get; set; }
        public List<(ExchangeId ExchangeId, string Symbol, MarketCategory Category)> Calls { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<MarketSnapshot> BuildSnapshotAsync(
            ExchangeId exchangeId,
            string symbol,
            MarketCategory category,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((exchangeId, symbol, category));
            CancellationTokens.Add(cancellationToken);
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class RecordingPolicyProvider(PolicyDefinition definition)
        : IRecommendationPolicyDefinitionProvider
    {
        public PolicyDefinition Definition { get; } = definition;
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<PolicyDefinition> Calls { get; } = [];

        public ValueTask<PolicyDefinition> GetAsync(
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            Calls.Add(Definition);
            return ValueTask.FromResult(Definition);
        }
    }

    private sealed class RecordingStabilityStateRepository : IRecommendationStabilityStateRepository
    {
        public Versioned<RecommendationStabilityStateSnapshot>? Pending { get; set; }
        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<Versioned<RecommendationStabilityStateSnapshot>?> GetAsync(
            UserId userId,
            PositionId positionId,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Pending);
        }

        public Task<ConcurrencyVersion> SaveAsync(
            UserId userId,
            PositionId positionId,
            RecommendationStabilityStateSnapshot state,
            RecommendationStabilityStateExpectation expectedState,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(ConcurrencyVersion.Initial);
        }

        public Task DeleteExpectedAsync(
            UserId userId,
            PositionId positionId,
            RecommendationStabilityStateExpectation expectedState,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublicationTransaction(List<string> order)
        : IRecommendationPublicationTransaction
    {
        public int PublishInitialCalls { get; private set; }
        public int ReplaceCalls { get; private set; }
        public int SavePendingCalls { get; private set; }
        public int ConfirmKeepCalls { get; private set; }
        public bool ThrowConcurrencyConflicts { get; set; }
        public RecommendationApplicationResultKind? LastKind { get; private set; }
        public List<CancellationToken> CancellationTokens { get; } = [];
        public List<string> Calls { get; } = [];

        public Task PublishInitialAsync(
            UserId userId,
            Recommendation successor,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default)
        {
            PublishInitialCalls++;
            if (ThrowConcurrencyConflicts)
                throw new ConcurrencyConflictException("test concurrency conflict");
            LastKind = RecommendationApplicationResultKind.Published;
            Calls.Add(nameof(PublishInitialAsync));
            order.Add("recommendation-publish");
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            UserId userId,
            Recommendation current,
            Recommendation successor,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default)
        {
            ReplaceCalls++;
            LastKind = RecommendationApplicationResultKind.Published;
            Calls.Add(nameof(ReplaceAsync));
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task SavePendingAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateSnapshot state,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default)
        {
            SavePendingCalls++;
            LastKind = RecommendationApplicationResultKind.PendingConfirmation;
            Calls.Add(nameof(SavePendingAsync));
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task ConfirmKeepExistingAsync(
            UserId userId,
            PositionId positionId,
            RecommendationCurrentExpectation expectedCurrent,
            RecommendationStabilityStateExpectation expectedPending,
            CancellationToken cancellationToken = default)
        {
            ConfirmKeepCalls++;
            LastKind = RecommendationApplicationResultKind.KeptExisting;
            Calls.Add(nameof(ConfirmKeepExistingAsync));
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEvaluationTransaction : IPositionEvaluationTransaction
    {
        public Task ExecuteAsync(
            UserId userId,
            PositionId positionId,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }

    private sealed class RecordingEvaluationEventOutbox : IApplicationEventOutbox
    {
        public List<IApplicationEvent> Events { get; } = [];

        public Task AddAsync(
            IApplicationEvent applicationEvent,
            CancellationToken cancellationToken = default)
        {
            Events.Add(applicationEvent);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(
            IReadOnlyCollection<IApplicationEvent> applicationEvents,
            CancellationToken cancellationToken = default)
        {
            Events.AddRange(applicationEvents);
            return Task.CompletedTask;
        }
    }
}
