using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Domain.Tests.Recommendations;

public sealed class RecommendationPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Canonical_policy_hash_is_stable_and_changes_with_behavior()
    {
        var first = PolicyDefinition.Default;
        var equivalent = new PolicyDefinition(
            new RuleVersion("recommendation-v1"),
            TimeSpan.FromMinutes(5),
            -10m,
            -5m,
            2m,
            5m,
            RecommendationConfidenceProfile.Default,
            RecommendationPriorityProfile.Default,
            AddAllowedPolicyLimits.Default);
        var changed = new PolicyDefinition(
            new RuleVersion("recommendation-v1"),
            TimeSpan.FromMinutes(5),
            -11m,
            -5m,
            2m,
            5m,
            RecommendationConfidenceProfile.Default,
            RecommendationPriorityProfile.Default,
            AddAllowedPolicyLimits.Default);

        equivalent.Identity.Should().Be(first.Identity);
        changed.Hash.Should().NotBe(first.Hash);
        first.Hash.Should().HaveLength(64);
    }

    [Fact]
    public void Deterministic_evaluation_repeats_all_semantic_fields()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);
        var evaluator = new RecommendationPolicy();

        var first = evaluator.Evaluate(assessment, policy, T0.AddMinutes(3));
        var second = evaluator.Evaluate(assessment, policy, T0.AddMinutes(3));

        first.Action.Action.Should().Be(second.Action.Action);
        first.Action.Confidence.Should().Be(second.Action.Confidence);
        first.Action.Priority.Should().Be(second.Action.Priority);
        first.AddDecision.Decision.Should().Be(second.AddDecision.Decision);
        first.CreatedAt.Should().Be(second.CreatedAt);
        first.ValidUntil.Should().Be(second.ValidUntil);
        first.Action.ReasonCodes.Should().Equal(second.Action.ReasonCodes);
        first.AddDecision.ReasonCodes.Should().Equal(second.AddDecision.ReasonCodes);
        first.AddDecision.MaximumAdditionalPositionValue
            .Should().Be(second.AddDecision.MaximumAdditionalPositionValue);
    }

    [Fact]
    public void Assessment_and_policy_identity_must_match()
    {
        var assessment = CreateAssessment(PolicyDefinition.Default, PositionTrendAlignment.Aligned);
        var otherPolicy = new PolicyDefinition(
            new RuleVersion("recommendation-v2"),
            PolicyDefinition.Default.ValidityPeriod,
            PolicyDefinition.Default.CloseLossThreshold,
            PolicyDefinition.Default.ReduceLossThreshold,
            PolicyDefinition.Default.ProtectProfitThreshold,
            PolicyDefinition.Default.TakePartialProfitThreshold,
            PolicyDefinition.Default.ConfidenceProfiles,
            PolicyDefinition.Default.PriorityProfiles,
            PolicyDefinition.Default.AddAllowedLimits);

        FluentActions.Invoking(
                () => new RecommendationPolicy().Evaluate(assessment, otherPolicy, T0.AddMinutes(3)))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(AssessmentDataQuality.Stale)]
    [InlineData(AssessmentDataQuality.Partial)]
    [InlineData(AssessmentDataQuality.Uncertain)]
    public void Degraded_data_always_returns_watch_and_do_not_add(AssessmentDataQuality quality)
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            quality,
            legacy: false,
            portfolioDecision: RiskIncreaseDecision.Blocked);

        var result = new RecommendationPolicy().Evaluate(assessment, policy, T0.AddMinutes(3));

        result.Action.Action.Should().Be(PositionAction.Watch);
        result.AddDecision.Decision.Should().Be(AddDecision.DoNotAdd);
        result.AddDecision.MaximumAdditionalPositionValue.Should().BeNull();
    }

    [Fact]
    public void Legacy_assessment_is_limited_before_policy_rules()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(
            policy,
            PositionTrendAlignment.Aligned,
            legacy: true);

        var result = new RecommendationPolicy().Evaluate(assessment, policy, T0.AddMinutes(3));

        result.Action.Action.Should().Be(PositionAction.Watch);
        result.AddDecision.Decision.Should().Be(AddDecision.DoNotAdd);
    }

    [Fact]
    public void Add_allowed_uses_the_smallest_conservative_headroom()
    {
        var policy = PolicyDefinition.Default;
        var assessment = CreateAssessment(policy, PositionTrendAlignment.Aligned);

        var result = new RecommendationPolicy().Evaluate(assessment, policy, T0.AddMinutes(3));

        result.Action.Action.Should().Be(PositionAction.Hold);
        result.AddDecision.Decision.Should().Be(AddDecision.AddAllowed);
        result.AddDecision.MaximumAdditionalPositionValue.Should().Be(1_000m);
        result.AddDecision.MaximumAdditionalQuantity.Should().Be(1_000m / 105m);
        result.AddDecision.Conditions.Should().NotBeNull();
    }

    [Fact]
    public void Explicit_precedence_supports_close_reduce_and_profit_management()
    {
        var policy = PolicyDefinition.Default;
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Adverse, pnlPercent: -10m),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.Close);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Adverse, pnlPercent: -5m),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.Reduce);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Aligned, pnlPercent: 6m, exhaustion: true,
                    resistanceNearby: true),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.TakePartialProfit);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Aligned, pnlPercent: 6m, exhaustion: true,
                    supportNearby: true, side: PositionSide.Short),
                policy,
                T0.AddMinutes(3))
                .Action.Action.Should().Be(PositionAction.TakePartialProfit);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Aligned, pnlPercent: 3m,
                    stopPosition: AssessmentPricePosition.Below),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.MoveStop);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.Aligned, pnlPercent: 3m, stopMissing: true),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.ProtectProfit);
        new RecommendationPolicy().Evaluate(
                CreateAssessment(policy, PositionTrendAlignment.FlatOrUnknown),
                policy,
                T0.AddMinutes(3))
            .Action.Action.Should().Be(PositionAction.Watch);
    }

    private static PositionAssessment CreateAssessment(
        PolicyDefinition policy,
        PositionTrendAlignment alignment,
        AssessmentDataQuality quality = AssessmentDataQuality.FreshCompleteReliable,
        bool legacy = false,
        RiskIncreaseDecision portfolioDecision = RiskIncreaseDecision.Allowed,
        decimal pnlPercent = 1m,
        bool exhaustion = false,
        bool resistanceNearby = false,
        bool supportNearby = false,
        bool stopMissing = false,
        AssessmentPricePosition stopPosition = AssessmentPricePosition.Below,
        PositionSide side = PositionSide.Long)
    {
        var inputVersions = new PositionAssessmentInputVersions(
            PositionId.New(),
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
                    stopMissing ? AssessmentStopState.Unavailable : AssessmentStopState.Protective,
                    stopMissing ? AssessmentPricePosition.Unavailable : stopPosition,
                    null,
                    false),
                new(100m, 4.7m, 0m, AssessmentPricePosition.Above) { IsProfitable = pnlPercent > 0m },
                new(40m, 61m, AssessmentLiquidationState.Far),
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
                    8_000m,
                    1_000m,
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
            pnlPercent > 0m ? ReasonCode.PnlPositive : ReasonCode.PnlNegative,
            stopMissing ? ReasonCode.StopMissing : ReasonCode.StopProtective,
            ReasonCode.LiquidationFar
        };
        if (exhaustion)
            reasons.Add(ReasonCode.MomentumExhaustion);
        if (resistanceNearby)
            reasons.Add(ReasonCode.ResistanceNearby);
        if (supportNearby)
            reasons.Add(ReasonCode.SupportNearby);
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
