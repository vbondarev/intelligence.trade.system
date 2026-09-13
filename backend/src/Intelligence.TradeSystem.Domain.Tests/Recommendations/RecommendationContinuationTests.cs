using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Tests.Recommendations;

public sealed class RecommendationContinuationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Safe_hold_with_unchanged_assessment_remains_valid_before_schedule()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeFalse();
    }

    [Fact]
    public void Degraded_safety_watch_does_not_self_invalidate()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            quality: AssessmentDataQuality.Stale,
            portfolioDecision: RiskIncreaseDecision.Blocked);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));

        recommendation.RecommendedAction.Should().Be(PositionAction.Watch);
        recommendation.AddDecision.Should().Be(AddDecision.DoNotAdd);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeFalse();
    }

    [Fact]
    public void Degraded_safety_watch_requests_reevaluation_when_quality_recovers()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var degraded = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            quality: AssessmentDataQuality.Stale,
            portfolioDecision: RiskIncreaseDecision.Blocked);
        var recommendation = CreateRecommendation(degraded, policy, T0.AddMinutes(3));
        var recovered = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            recovered,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Pnl_availability_change_invalidates_hold()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var available = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId);
        var recommendation = CreateRecommendation(available, policy, T0.AddMinutes(3));
        var unavailable = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: null);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            unavailable,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeTrue();
        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Pnl_recovery_requests_reevaluation_for_pnl_unavailable_watch()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var unavailable = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: null);
        var recommendation = CreateRecommendation(unavailable, policy, T0.AddMinutes(3));
        var available = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId);

        recommendation.RecommendedAction.Should().Be(PositionAction.Watch);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            available,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeTrue();
    }

    [Theory]
    [InlineData(PositionSide.Long, AssessmentPricePosition.Below)]
    [InlineData(PositionSide.Short, AssessmentPricePosition.Above)]
    public void Move_stop_uses_side_aware_profit_protection(
        PositionSide side,
        AssessmentPricePosition nonProtectivePosition)
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            side: side,
            stopPosition: nonProtectivePosition);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));

        recommendation.RecommendedAction.Should().Be(PositionAction.MoveStop);
        var unchanged = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            T0.AddMinutes(3));
        unchanged.ShouldReevaluate.Should().BeFalse();

        var protectedPosition = side == PositionSide.Long
            ? AssessmentPricePosition.Above
            : AssessmentPricePosition.Below;
        var changed = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            side: side,
            stopPosition: protectedPosition);
        var changedResult = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            changed,
            policy,
            T0.AddMinutes(3));

        changedResult.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Protect_profit_reacts_when_missing_stop_appears_non_protective()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var missingStop = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            stopMissing: true);
        var recommendation = CreateRecommendation(missingStop, policy, T0.AddMinutes(3));
        var stopAppears = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            stopPosition: AssessmentPricePosition.Below);

        recommendation.RecommendedAction.Should().Be(PositionAction.ProtectProfit);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            stopAppears,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Add_allowed_expiry_invalidates_both_independent_decisions()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            stopPosition: AssessmentPricePosition.Above);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));

        recommendation.AddDecision.Should().Be(AddDecision.AddAllowed);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            recommendation.ValidUntil);

        result.IsExpired.Should().BeTrue();
        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeTrue();
        result.AddDecisionInvalidated.Should().BeTrue();
        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Reduced_capacity_invalidates_add_decision_but_not_action()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            stopPosition: AssessmentPricePosition.Above,
            availableCapital: 2_000m);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));
        var reduced = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            stopPosition: AssessmentPricePosition.Above,
            availableCapital: 1_000m);

        recommendation.AddDecision.Should().Be(AddDecision.AddAllowed);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            reduced,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeFalse();
        result.AddDecisionInvalidated.Should().BeTrue();
    }

    [Fact]
    public void Increased_capacity_requests_new_recommendation_without_increasing_old_maximum()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            stopPosition: AssessmentPricePosition.Above,
            availableCapital: 2_000m);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));
        var increased = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            stopPosition: AssessmentPricePosition.Above,
            availableCapital: 8_000m,
            currentPositionValue: 500m);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            increased,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.AddDecisionInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeTrue();
        recommendation.MaximumAdditionalPositionValue.Should().BeLessThan(
            AdditionalPositionCapacityCalculator.Calculate(
                increased.Result.PortfolioRisk,
                increased.Result.CurrentPrice,
                policy.AddAllowedLimits).MaximumPositionValue!.Value);
    }

    [Fact]
    public void Policy_identity_condition_uses_its_persisted_required_identity()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));
        var changedPolicy = new PolicyDefinition(
            new RuleVersion("recommendation-v1"),
            policy.ValidityPeriod,
            -11m,
            policy.ReduceLossThreshold,
            policy.ProtectProfitThreshold,
            policy.TakePartialProfitThreshold,
            policy.ConfidenceProfiles,
            policy.PriorityProfiles,
            policy.AddAllowedLimits,
            policy.ReevaluationProfile);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            changedPolicy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeTrue();
    }

    [Fact]
    public void Restore_structured_rejects_mismatched_continuation_policy_identity()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);
        var evaluation = new RecommendationPolicy().Evaluate(assessment, policy, T0.AddMinutes(3));
        var structured = Recommendation.Create(assessment, evaluation);
        var wrongIdentity = PolicyConfigurationIdentity.From(
            policy.Identity.Version,
            new string('B', policy.Identity.Hash.Length));
        var corruptedPlan = new RecommendationContinuationPlan(
            evaluation.ContinuationPlan.InvalidationConditions.Select(condition =>
                condition is PolicyIdentityCondition
                    ? new PolicyIdentityCondition(
                        RecommendationContinuationConditionScope.Recommendation,
                        wrongIdentity)
                    : condition),
            evaluation.ContinuationPlan.ReevaluationConditions,
            evaluation.ContinuationPlan.CreatedAt,
            evaluation.ContinuationPlan.ValidUntil,
            evaluation.ContinuationPlan.NextEvaluationAt);

        FluentActions.Invoking(() => Recommendation.RestoreStructured(
                RecommendationId.New(),
                assessment,
                evaluation.Action,
                evaluation.AddDecision,
                policy.Identity,
                structured.ReasonCodes,
                evaluation.CreatedAt,
                evaluation.ValidUntil,
                RecommendationStatus.Active,
                null,
                null,
                null,
                null,
                null,
                corruptedPlan))
            .Should().Throw<ArgumentException>()
            .WithMessage("*policy identity*");
    }

    [Fact]
    public void Legacy_recommendation_without_plan_is_conservatively_invalidated_when_policy_changes()
    {
        var oldPolicy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            oldPolicy,
            PositionTrendAlignment.Aligned,
            legacy: true);
        var recommendation = Recommendation.RestoreLegacy(
            RecommendationId.New(),
            assessment,
            PositionAction.Close,
            AddDecision.DoNotAdd,
            new RuleVersion("policy-old"),
            assessment.ReasonCodes,
            T0.AddMinutes(3),
            T0.AddMinutes(30),
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        var currentPolicy = new PolicyDefinition(
            new RuleVersion("policy-new"),
            oldPolicy.ValidityPeriod,
            oldPolicy.CloseLossThreshold,
            oldPolicy.ReduceLossThreshold,
            oldPolicy.ProtectProfitThreshold,
            oldPolicy.TakePartialProfitThreshold,
            oldPolicy.ConfidenceProfiles,
            oldPolicy.PriorityProfiles,
            oldPolicy.AddAllowedLimits,
            oldPolicy.ReevaluationProfile);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            currentPolicy,
            T0.AddMinutes(4));

        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeTrue();
        result.ShouldReevaluate.Should().BeTrue();
        result.TriggeredConditions.Should().ContainItemsAssignableTo<ContinuationContextUnavailableCondition>();
    }

    [Fact]
    public void Pre_e07_structured_add_allowed_without_plan_invalidates_add_decision()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            stopPosition: AssessmentPricePosition.Above);
        var evaluation = new RecommendationPolicy().Evaluate(assessment, policy, T0.AddMinutes(3));
        var structured = Recommendation.Create(assessment, evaluation);
        var recommendation = Recommendation.RestoreStructured(
            RecommendationId.New(),
            assessment,
            evaluation.Action,
            evaluation.AddDecision,
            evaluation.PolicyIdentity,
            structured.ReasonCodes,
            evaluation.CreatedAt,
            evaluation.ValidUntil,
            RecommendationStatus.Active,
            null,
            null,
            null,
            null,
            null);
        var changedPolicy = new PolicyDefinition(
            new RuleVersion("recommendation-v1"),
            policy.ValidityPeriod,
            -11m,
            policy.ReduceLossThreshold,
            policy.ProtectProfitThreshold,
            policy.TakePartialProfitThreshold,
            policy.ConfidenceProfiles,
            policy.PriorityProfiles,
            policy.AddAllowedLimits,
            policy.ReevaluationProfile);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            changedPolicy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeTrue();
        result.ActionInvalidated.Should().BeTrue();
        result.AddDecisionInvalidated.Should().BeTrue();
        result.ShouldReevaluate.Should().BeTrue();
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("watch")]
    [InlineData("close")]
    [InlineData("reduce")]
    [InlineData("partial")]
    [InlineData("move-stop")]
    [InlineData("protect-profit")]
    public void Same_assessment_does_not_self_invalidate_any_action(string scenario)
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = scenario switch
        {
            "hold" => CreateAssessment(policy, PositionTrendAlignment.Aligned, positionId),
            "watch" => CreateAssessment(
                policy,
                PositionTrendAlignment.Aligned,
                positionId,
                lowVolume: true),
            "close" => CreateAssessment(
                policy,
                PositionTrendAlignment.Aligned,
                positionId,
                liquidationState: AssessmentLiquidationState.Near),
            "reduce" => CreateAssessment(
                policy,
                PositionTrendAlignment.Adverse,
                positionId,
                pnlPercent: -6m),
            "partial" => CreateAssessment(
                policy,
                PositionTrendAlignment.Aligned,
                positionId,
                pnlPercent: 6m,
                exhaustion: true,
                resistanceNearby: true),
            "move-stop" => CreateAssessment(
                policy,
                PositionTrendAlignment.Aligned,
                positionId,
                pnlPercent: 3m),
            "protect-profit" => CreateAssessment(
                policy,
                PositionTrendAlignment.Aligned,
                positionId,
                pnlPercent: 3m,
                stopMissing: true),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario)
        };
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            T0.AddMinutes(3));

        result.IsInvalidated.Should().BeFalse();
        result.ShouldReevaluate.Should().BeFalse();
    }

    [Fact]
    public void Reduce_requests_immediate_reevaluation_when_close_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var reduceAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Adverse,
            positionId,
            pnlPercent: -6m);
        var recommendation = CreateRecommendation(reduceAssessment, policy, T0.AddMinutes(3));
        var closeAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Adverse,
            positionId,
            pnlPercent: -12m);

        recommendation.RecommendedAction.Should().Be(PositionAction.Reduce);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            closeAssessment,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
        result.ActionInvalidated.Should().BeFalse();
        result.TriggeredConditions.Should().ContainItemsAssignableTo<HigherPriorityActionsCondition>();
    }

    [Fact]
    public void Reduce_requests_immediate_reevaluation_when_liquidation_becomes_near()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var reduceAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Adverse,
            positionId,
            pnlPercent: -6m);
        var recommendation = CreateRecommendation(reduceAssessment, policy, T0.AddMinutes(3));
        var liquidationNear = CreateAssessment(
            policy,
            PositionTrendAlignment.Adverse,
            positionId,
            pnlPercent: -6m,
            liquidationState: AssessmentLiquidationState.Near);

        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            liquidationNear,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Take_partial_profit_requests_immediate_reevaluation_when_close_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var partialAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 6m,
            exhaustion: true,
            resistanceNearby: true);
        var recommendation = CreateRecommendation(partialAssessment, policy, T0.AddMinutes(3));
        var liquidationNear = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 6m,
            exhaustion: true,
            resistanceNearby: true,
            liquidationState: AssessmentLiquidationState.Near);

        recommendation.RecommendedAction.Should().Be(PositionAction.TakePartialProfit);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            liquidationNear,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Move_stop_requests_immediate_reevaluation_when_reduce_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var moveStopAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m);
        var recommendation = CreateRecommendation(moveStopAssessment, policy, T0.AddMinutes(3));
        var reduceAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Adverse,
            positionId,
            pnlPercent: -6m);

        recommendation.RecommendedAction.Should().Be(PositionAction.MoveStop);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            reduceAssessment,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Protect_profit_requests_immediate_reevaluation_when_move_stop_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var protectProfitAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            stopMissing: true);
        var recommendation = CreateRecommendation(protectProfitAssessment, policy, T0.AddMinutes(3));
        var moveStopAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m,
            stopPosition: AssessmentPricePosition.Below);

        recommendation.RecommendedAction.Should().Be(PositionAction.ProtectProfit);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            moveStopAssessment,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Watch_requests_immediate_reevaluation_when_close_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var watchAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            lowVolume: true);
        var recommendation = CreateRecommendation(watchAssessment, policy, T0.AddMinutes(3));
        var closeAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            lowVolume: true,
            liquidationState: AssessmentLiquidationState.Near);

        recommendation.RecommendedAction.Should().Be(PositionAction.Watch);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            closeAssessment,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Hold_requests_immediate_reevaluation_when_move_stop_becomes_required()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var holdAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 1m);
        var recommendation = CreateRecommendation(holdAssessment, policy, T0.AddMinutes(3));
        var moveStopAssessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            positionId,
            pnlPercent: 3m);

        recommendation.RecommendedAction.Should().Be(PositionAction.Hold);
        var result = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            moveStopAssessment,
            policy,
            T0.AddMinutes(3));

        result.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Next_evaluation_boundary_is_inclusive()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);
        var recommendation = CreateRecommendation(assessment, policy, T0.AddMinutes(3));
        var before = recommendation.NextEvaluationAt!.Value.AddTicks(-1);

        var beforeResult = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            before);
        var atResult = RecommendationContinuationEvaluator.Evaluate(
            recommendation,
            assessment,
            policy,
            recommendation.NextEvaluationAt.Value);

        beforeResult.ShouldReevaluate.Should().BeFalse();
        atResult.ShouldReevaluate.Should().BeTrue();
    }

    [Fact]
    public void Plan_rejects_null_duplicate_and_legacy_conditions()
    {
        var expiry = T0.AddMinutes(30);
        var common = new RecommendationContinuationCondition[]
        {
            new RecommendationExpiryCondition(expiry),
            new PolicyIdentityCondition(
                RecommendationContinuationConditionScope.Recommendation,
                PolicyDefinition.Default.Identity)
        };

        FluentActions.Invoking(() => new RecommendationContinuationPlan(
                [.. common, null!],
                [new TrendAlignmentCondition(
                    RecommendationContinuationConditionScope.Action,
                    PositionTrendAlignment.Aligned)],
                T0,
                expiry,
                T0.AddMinutes(2)))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => new RecommendationContinuationPlan(
                [.. common, new PolicyIdentityCondition(
                    RecommendationContinuationConditionScope.Recommendation,
                    PolicyDefinition.Default.Identity)],
                [new TrendAlignmentCondition(
                    RecommendationContinuationConditionScope.Action,
                    PositionTrendAlignment.Aligned)],
                T0,
                expiry,
                T0.AddMinutes(2)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Typed_conditions_reject_invalid_payloads()
    {
        FluentActions.Invoking(() => new LiquidationDistanceCondition(
                RecommendationContinuationConditionScope.Action,
                0m))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new StopStateCondition(
                RecommendationContinuationConditionScope.Action,
                (AssessmentStopState)99))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PnlThresholdCondition(
                RecommendationContinuationConditionScope.Action,
                (RecommendationPnlComparison)99,
                1m))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new PolicyIdentityCondition(
                RecommendationContinuationConditionScope.Action,
                PolicyConfigurationIdentity.Legacy))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RecommendationExpiryCondition(default))
            .Should().Throw<ArgumentException>();
    }

    private static Recommendation CreateRecommendation(
        PositionAssessment assessment,
        PolicyDefinition policy,
        DateTimeOffset asOf)
    {
        var evaluation = new RecommendationPolicy().Evaluate(assessment, policy, asOf);
        return Recommendation.Create(assessment, evaluation);
    }

    private static PositionAssessment CreateAssessment(
        PolicyDefinition policy,
        PositionTrendAlignment alignment,
        PositionId? positionId = null,
        AssessmentDataQuality quality = AssessmentDataQuality.FreshCompleteReliable,
        bool legacy = false,
        RiskIncreaseDecision portfolioDecision = RiskIncreaseDecision.Allowed,
        decimal? pnlPercent = 1m,
        bool exhaustion = false,
        bool resistanceNearby = false,
        bool supportNearby = false,
        bool stopMissing = false,
        AssessmentPricePosition stopPosition = AssessmentPricePosition.Below,
        PositionSide side = PositionSide.Long,
        AssessmentStopState stopState = AssessmentStopState.Protective,
        bool lowVolume = false,
        AssessmentLiquidationState liquidationState = AssessmentLiquidationState.Far,
        decimal? liquidationDistance = 61m,
        decimal? availableCapital = 8_000m,
        decimal? currentPositionValue = 1_000m)
    {
        var id = positionId ?? PositionId.New();
        var inputVersions = new PositionAssessmentInputVersions(
            id,
            ExchangeAccountId.New(),
            InstrumentId.From("BTCUSDT"),
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
            policy.Identity,
            policy.Identity);
        var result = legacy
            ? PositionAssessmentResult.Legacy(portfolioDecision)
            : new PositionAssessmentResult(
                side,
                105m,
                new(AssessmentTrendDirection.Bullish, alignment, 0.8m, "4h"),
                new(50m, true, AssessmentMomentumState.Normal, exhaustion),
                new(1m, 1m, true, false),
                new(105m, 99m, 1m, 0.7m, 110m, 1m, 0.7m),
                new(pnlPercent, pnlPercent, 100m, 105m, AssessmentPricePosition.Above)
                {
                    IsFavorable = pnlPercent > 0m
                },
                new(
                    stopMissing ? null : 90m,
                    14m,
                    stopMissing ? null : stopPosition == AssessmentPricePosition.Above ? 2m : -10m,
                    stopMissing ? AssessmentStopState.Unavailable : stopState,
                    stopMissing ? AssessmentPricePosition.Unavailable : stopPosition,
                    null,
                    false),
                new(100m, 4.7m, 0m, AssessmentPricePosition.Above)
                {
                    IsProfitable = pnlPercent > 0m
                },
                new(40m, liquidationDistance, liquidationState),
                new(
                    portfolioDecision,
                    80m,
                    50m,
                    10m,
                    pnlPercent,
                    2_000m,
                    true,
                    true,
                    10_000m,
                    availableCapital,
                    currentPositionValue,
                    10m,
                    10m,
                    100m,
                    25m),
                new(quality, quality));

        if (legacy)
        {
            return PositionAssessment.Create(
                inputVersions,
                new RuleVersion("assessment-v1"),
                portfolioDecision == RiskIncreaseDecision.Allowed
                    ? RiskIncreasePolicyResult.Allowed()
                    : RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
                [],
                T0.AddMinutes(2),
                T0.AddHours(1));
        }

        var reasons = new List<ReasonCode>
        {
            alignment switch
            {
                PositionTrendAlignment.Aligned => ReasonCode.TrendAligned,
                PositionTrendAlignment.Adverse => ReasonCode.TrendAdverse,
                _ => ReasonCode.TrendFlatOrUnknown
            },
            ReasonCode.MomentumNormal,
            pnlPercent is null
                ? ReasonCode.PnlUnavailable
                : pnlPercent > 0m ? ReasonCode.PnlPositive : ReasonCode.PnlNegative,
            stopMissing ? ReasonCode.StopMissing : ReasonCode.StopProtective,
            ReasonCode.LiquidationFar
        };
        if (exhaustion)
            reasons.Add(ReasonCode.MomentumExhaustion);
        if (resistanceNearby)
            reasons.Add(ReasonCode.ResistanceNearby);
        if (supportNearby)
            reasons.Add(ReasonCode.SupportNearby);
        if (lowVolume)
            reasons.Add(ReasonCode.LowVolume);
        if (liquidationState == AssessmentLiquidationState.Near)
            reasons.Add(ReasonCode.LiquidationNearby);
        if (quality != AssessmentDataQuality.FreshCompleteReliable)
            reasons.Add(ReasonCode.MarketDataUncertain);

        return PositionAssessment.Create(
            inputVersions,
            new RuleVersion("assessment-v1"),
            portfolioDecision == RiskIncreaseDecision.Allowed
                ? RiskIncreasePolicyResult.Allowed()
                : RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]),
            result,
            reasons,
            T0.AddMinutes(2),
            T0.AddHours(1));
    }
}
