using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Domain.Tests.Recommendations;

public sealed class RecommendationStabilityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Technical_metadata_and_confidence_changes_are_duplicate()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var currentAssessment = CreateAssessment(policy, positionId);
        var current = CreateRecommendation(currentAssessment, policy, T0.AddMinutes(3));
        var candidateAssessment = CreateAssessment(policy, positionId);
        var baseCandidate = policy.Evaluate(candidateAssessment, policy, T0.AddMinutes(4));
        var candidate = new RecommendationPolicyEvaluation(
            baseCandidate.PolicyIdentity,
            new RecommendedActionDecision(
                baseCandidate.Action.Action,
                0.71m,
                baseCandidate.Action.Priority,
                baseCandidate.Action.ReasonCodes),
            baseCandidate.AddDecision,
            baseCandidate.ContinuationPlan,
            baseCandidate.CreatedAt,
            baseCandidate.ValidUntil,
            baseCandidate.InheritedReasonCodes);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.KeepExisting);
        result.Reason.Should().Be(RecommendationStabilityReason.Duplicate);
        result.NextState.Should().BeNull();
    }

    [Theory]
    [InlineData(PositionAction.Hold, PositionAction.Reduce)]
    [InlineData(PositionAction.Hold, PositionAction.Close)]
    [InlineData(PositionAction.Watch, PositionAction.Close)]
    [InlineData(PositionAction.Watch, PositionAction.Reduce)]
    [InlineData(PositionAction.ProtectProfit, PositionAction.Reduce)]
    [InlineData(PositionAction.MoveStop, PositionAction.Reduce)]
    [InlineData(PositionAction.TakePartialProfit, PositionAction.Reduce)]
    [InlineData(PositionAction.Reduce, PositionAction.Close)]
    public void Stronger_action_is_published_immediately(
        PositionAction currentAction,
        PositionAction candidateAction)
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, currentAction),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, candidateAction),
            policy,
            T0.AddMinutes(4));
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            10,
            TimeSpan.FromHours(1),
            10);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            profile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
    }

    [Fact]
    public void Degraded_safety_candidate_bypasses_stability()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessment(
                policy,
                positionId,
                quality: AssessmentDataQuality.Stale,
                portfolioDecision: RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            new RecommendationStabilityProfile(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                10,
                TimeSpan.FromHours(1),
                10),
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.SafetyEscalation);
    }

    [Fact]
    public void Critical_priority_escalation_is_published_immediately()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var currentAssessment = CreateAssessment(policy, positionId);
        var current = CreateRecommendation(currentAssessment, policy, T0.AddMinutes(3));
        var candidateAssessment = CreateAssessment(policy, positionId);
        var baseCandidate = policy.Evaluate(candidateAssessment, policy, T0.AddMinutes(4));
        var candidate = new RecommendationPolicyEvaluation(
            baseCandidate.PolicyIdentity,
            new RecommendedActionDecision(
                baseCandidate.Action.Action,
                baseCandidate.Action.Confidence,
                RecommendationPriority.Critical,
                baseCandidate.Action.ReasonCodes),
            baseCandidate.AddDecision,
            baseCandidate.ContinuationPlan,
            baseCandidate.CreatedAt,
            baseCandidate.ValidUntil,
            baseCandidate.InheritedReasonCodes);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            new RecommendationStabilityProfile(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                10,
                TimeSpan.FromHours(1),
                10),
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.PriorityEscalation);
    }

    [Fact]
    public void Add_permission_revocation_is_immediate()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessment(
                policy,
                positionId,
                portfolioDecision: RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));

        current.AddDecision.Should().Be(AddDecision.AddAllowed);
        candidate.AddDecision.Decision.Should().Be(AddDecision.DoNotAdd);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            new RecommendationStabilityProfile(
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(1),
                10,
                TimeSpan.FromHours(1),
                10),
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionRevoked);
    }

    [Fact]
    public void Improvement_requires_observation_and_time_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Close),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(2),
            2,
            TimeSpan.FromMinutes(3),
            3);
        var stability = new RecommendationStabilityPolicy();

        var first = stability.Evaluate(current, candidate, null, profile, T0.AddMinutes(4));
        var second = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(5)),
            first.NextState,
            profile,
            T0.AddMinutes(5));
        var confirmed = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(6)),
            second.NextState,
            profile,
            T0.AddMinutes(6));

        first.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        first.NextState!.ConsecutiveObservations.Should().Be(1);
        second.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        second.NextState!.ConsecutiveObservations.Should().Be(2);
        confirmed.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
    }

    [Fact]
    public void Alternating_candidates_do_not_accumulate_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            2,
            TimeSpan.FromMinutes(3),
            3);
        var stability = new RecommendationStabilityPolicy();
        var watch = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var hold = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(5));

        var firstWatch = stability.Evaluate(current, watch, null, profile, T0.AddMinutes(4));
        var backToHold = stability.Evaluate(current, hold, firstWatch.NextState, profile, T0.AddMinutes(5));
        var secondWatch = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(6)),
            backToHold.NextState,
            profile,
            T0.AddMinutes(6));

        firstWatch.NextState!.ConsecutiveObservations.Should().Be(1);
        backToHold.Kind.Should().Be(RecommendationStabilityDecisionKind.KeepExisting);
        backToHold.NextState.Should().BeNull();
        secondWatch.NextState!.ConsecutiveObservations.Should().Be(1);
        secondWatch.NextState.FirstObservedAt.Should().Be(T0.AddMinutes(6));
    }

    [Fact]
    public void Changed_pending_candidate_starts_a_new_state()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Close, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            2,
            TimeSpan.FromMinutes(3),
            3);
        var stability = new RecommendationStabilityPolicy();
        var hold = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var protectProfit = policy.Evaluate(
            CreateAssessment(
                policy,
                positionId,
                pnlPercent: 3m,
                stopMissing: true,
                portfolioDecision: RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(5));

        var first = stability.Evaluate(current, hold, null, profile, T0.AddMinutes(4));
        var changed = stability.Evaluate(current, protectProfit, first.NextState, profile, T0.AddMinutes(5));

        changed.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        changed.Reason.Should().Be(RecommendationStabilityReason.CandidateChanged);
        changed.NextState!.ConsecutiveObservations.Should().Be(1);
        changed.NextState.FirstObservedAt.Should().Be(T0.AddMinutes(5));
    }

    [Fact]
    public void Confirmed_candidate_waits_for_replacement_cooldown()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(1),
            1,
            TimeSpan.FromMinutes(3),
            3);
        var stability = new RecommendationStabilityPolicy();

        var first = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(4)),
            null,
            profile,
            T0.AddMinutes(4));
        var duringCooldown = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(5)),
            first.NextState,
            profile,
            T0.AddMinutes(5));
        var afterCooldown = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(14)),
            duringCooldown.NextState,
            profile,
            T0.AddMinutes(14));

        duringCooldown.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        duringCooldown.Reason.Should().Be(RecommendationStabilityReason.WithinCooldown);
        afterCooldown.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
    }

    [Fact]
    public void Add_permission_grant_uses_stricter_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessment(policy, positionId, portfolioDecision: RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var stability = new RecommendationStabilityPolicy();
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            2,
            TimeSpan.FromMinutes(3),
            3);

        var first = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
                policy,
                T0.AddMinutes(4)),
            null,
            profile,
            T0.AddMinutes(4));
        var second = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
                policy,
                T0.AddMinutes(5)),
            first.NextState,
            profile,
            T0.AddMinutes(5));
        var third = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
                policy,
                T0.AddMinutes(7)),
            second.NextState,
            profile,
            T0.AddMinutes(7));

        first.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
        second.NextState!.ConsecutiveObservations.Should().Be(2);
        second.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        third.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        third.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
    }

    [Fact]
    public void Add_allowed_capacity_decrease_is_immediate_and_increase_is_delayed()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1000m, 10m);
        var stability = new RecommendationStabilityPolicy();
        var profile = new RecommendationStabilityProfile(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            2,
            TimeSpan.FromMinutes(2),
            2);

        var decrease = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 800m, 8m),
            null,
            profile,
            T0.AddMinutes(4));
        var increase = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1200m, 12m),
            null,
            profile,
            T0.AddMinutes(4));

        decrease.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        decrease.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
        increase.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        increase.NextState!.ConsecutiveObservations.Should().Be(1);

        stability.Evaluate(
                current,
                CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1000m, 10m),
                null,
                profile,
                T0.AddMinutes(4))
            .Reason.Should().Be(RecommendationStabilityReason.Duplicate);
    }

    [Fact]
    public void Policy_identity_change_invalidates_pending_candidate()
    {
        var oldPolicy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessmentForAction(
            oldPolicy,
            positionId,
            PositionAction.Hold,
            RiskIncreaseDecision.Blocked);
        var current = CreateRecommendation(assessment, oldPolicy, T0.AddMinutes(3));
        var baseCandidate = oldPolicy.Evaluate(assessment, oldPolicy, T0.AddMinutes(4));
        var newIdentity = PolicyConfigurationIdentity.From("recommendation-v2", new string('A', 64));
        var candidate = new RecommendationPolicyEvaluation(
            newIdentity,
            baseCandidate.Action,
            baseCandidate.AddDecision,
            baseCandidate.ContinuationPlan,
            baseCandidate.CreatedAt,
            baseCandidate.ValidUntil,
            baseCandidate.InheritedReasonCodes);
        var pending = new RecommendationStabilityState(
            RecommendationSemanticState.From(baseCandidate),
            T0.AddMinutes(3),
            T0.AddMinutes(3),
            4);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            pending,
            oldPolicy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.PolicyChanged);
        result.NextState.Should().BeNull();
    }

    [Fact]
    public void Expired_and_terminal_current_recommendations_do_not_suppress_candidate()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var stability = new RecommendationStabilityPolicy();

        stability.Evaluate(
                current,
                candidate,
                null,
                policy.StabilityProfile,
                current.ValidUntil)
            .Reason.Should().Be(RecommendationStabilityReason.CurrentExpired);

        var dismissed = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        dismissed.Dismiss(T0.AddMinutes(4));
        stability.Evaluate(dismissed, candidate, null, policy.StabilityProfile, T0.AddMinutes(5))
            .Reason.Should().Be(RecommendationStabilityReason.CurrentInactive);

        var acknowledged = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        acknowledged.Acknowledge(T0.AddMinutes(4));
        stability.Evaluate(acknowledged, candidate, null, policy.StabilityProfile, T0.AddMinutes(5))
            .Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);

        var superseded = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        var successor = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        superseded.SupersedeBy(successor);
        stability.Evaluate(superseded, candidate, null, policy.StabilityProfile, T0.AddMinutes(5))
            .Reason.Should().Be(RecommendationStabilityReason.CurrentInactive);
    }

    [Fact]
    public void Semantic_state_is_order_independent_and_pending_state_is_immutable()
    {
        var identity = PolicyDefinition.Default.Identity;
        var first = new RecommendationSemanticState(
            PositionAction.Hold,
            AddDecision.DoNotAdd,
            RecommendationPriority.Normal,
            [ReasonCode.PnlPositive, ReasonCode.TrendAligned],
            [ReasonCode.AddBlockedByAction, ReasonCode.AddBlockedByMomentum],
            identity,
            inheritedReasonCodes: [ReasonCode.PortfolioDataStale, ReasonCode.InsufficientFreeCapital]);
        var second = new RecommendationSemanticState(
            PositionAction.Hold,
            AddDecision.DoNotAdd,
            RecommendationPriority.Normal,
            [ReasonCode.TrendAligned, ReasonCode.PnlPositive],
            [ReasonCode.AddBlockedByMomentum, ReasonCode.AddBlockedByAction],
            identity,
            inheritedReasonCodes: [ReasonCode.InsufficientFreeCapital, ReasonCode.PortfolioDataStale]);

        var oldState = new RecommendationStabilityState(first, T0.AddMinutes(4), T0.AddMinutes(4), 1);
        var nextState = new RecommendationStabilityState(
            second,
            oldState.FirstObservedAt,
            T0.AddMinutes(5),
            oldState.ConsecutiveObservations + 1);

        first.Should().Be(second);
        oldState.ConsecutiveObservations.Should().Be(1);
        oldState.LastObservedAt.Should().Be(T0.AddMinutes(4));
        nextState.Should().NotBeSameAs(oldState);
    }

    [Fact]
    public void Candidate_must_be_valid_at_as_of()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var stability = new RecommendationStabilityPolicy();

        FluentActions.Invoking(() => stability.Evaluate(
                null,
                candidate,
                null,
                policy.StabilityProfile,
                candidate.ValidUntil))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => stability.Evaluate(
                null,
                candidate,
                null,
                policy.StabilityProfile,
                candidate.ValidUntil.AddTicks(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => stability.Evaluate(
                null,
                candidate,
                null,
                policy.StabilityProfile,
                candidate.CreatedAt.AddTicks(-1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Candidate_must_be_newer_than_active_current_and_not_precede_acknowledgement()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessment(policy, positionId),
            policy,
            T0.AddMinutes(3));
        var stability = new RecommendationStabilityPolicy();
        var equal = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var older = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(2).AddSeconds(30));

        FluentActions.Invoking(() => stability.Evaluate(
                current,
                equal,
                null,
                policy.StabilityProfile,
                T0.AddMinutes(4)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => stability.Evaluate(
                current,
                older,
                null,
                policy.StabilityProfile,
                T0.AddMinutes(4)))
            .Should().Throw<ArgumentException>();

        current.Acknowledge(T0.AddMinutes(4));
        var beforeAcknowledgement = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3).AddSeconds(30));
        FluentActions.Invoking(() => stability.Evaluate(
                current,
                beforeAcknowledgement,
                null,
                policy.StabilityProfile,
                T0.AddMinutes(5)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Pending_observations_are_chronological_and_same_timestamp_replay_is_idempotent()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var stability = new RecommendationStabilityPolicy();
        var profile = ConfirmationProfile();
        var watchAtFour = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));
        var first = stability.Evaluate(current, watchAtFour, null, profile, T0.AddMinutes(4));

        FluentActions.Invoking(() => stability.Evaluate(
                current,
                watchAtFour,
                first.NextState,
                profile,
                T0.AddMinutes(3).AddSeconds(30)))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => stability.Evaluate(
                current,
                policy.Evaluate(
                    CreateAssessmentForAction(policy, positionId, PositionAction.ProtectProfit, RiskIncreaseDecision.Blocked),
                    policy,
                    T0.AddMinutes(4)),
                first.NextState,
                profile,
                T0.AddMinutes(4)))
            .Should().Throw<ArgumentException>();

        var replay = stability.Evaluate(current, watchAtFour, first.NextState, profile, T0.AddMinutes(4));
        replay.NextState.Should().BeSameAs(first.NextState);
        replay.NextState!.ConsecutiveObservations.Should().Be(1);

        var nextObservation = stability.Evaluate(
            current,
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(5)),
            replay.NextState,
            profile,
            T0.AddMinutes(5));
        nextObservation.NextState!.ConsecutiveObservations.Should().Be(2);
    }

    [Fact]
    public void Critical_priority_cannot_bypass_add_permission_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var baseCandidate = policy.Evaluate(
            CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
            policy,
            T0.AddMinutes(4));
        var candidate = WithActionPriority(baseCandidate, RecommendationPriority.Critical);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
        result.NextState!.ConsecutiveObservations.Should().Be(1);
    }

    [Fact]
    public void Policy_change_does_not_bypass_add_permission_confirmation()
    {
        var oldPolicy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(oldPolicy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            oldPolicy,
            T0.AddMinutes(3));
        var baseCandidate = oldPolicy.Evaluate(
            CreateAssessment(oldPolicy, positionId, stopPosition: AssessmentPricePosition.Above),
            oldPolicy,
            T0.AddMinutes(4));
        var candidate = WithIdentity(baseCandidate, "recommendation-v2");

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            oldPolicy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
        result.NextState!.ConsecutiveObservations.Should().Be(1);
        result.NextState.FirstObservedAt.Should().Be(T0.AddMinutes(4));
    }

    [Fact]
    public void Policy_change_does_not_bypass_add_allowed_capacity_increase()
    {
        var oldPolicy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(oldPolicy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, oldPolicy, T0.AddMinutes(3), 800m, 8m);
        var candidate = WithIdentity(
            CreateCapacityEvaluation(assessment, oldPolicy, T0.AddMinutes(4), 1200m, 12m),
            "recommendation-v2");

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            oldPolicy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
    }

    [Fact]
    public void Policy_change_with_equal_risk_decision_is_published_without_old_pending_state()
    {
        var oldPolicy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessmentForAction(
            oldPolicy,
            positionId,
            PositionAction.Hold,
            RiskIncreaseDecision.Blocked);
        var current = CreateRecommendation(assessment, oldPolicy, T0.AddMinutes(3));
        var candidate = WithIdentity(
            oldPolicy.Evaluate(assessment, oldPolicy, T0.AddMinutes(4)),
            "recommendation-v2");
        var pending = new RecommendationStabilityState(
            RecommendationSemanticState.From(oldPolicy.Evaluate(assessment, oldPolicy, T0.AddMinutes(3))),
            T0.AddMinutes(3),
            T0.AddMinutes(3),
            5);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            pending,
            oldPolicy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.PolicyChanged);
        result.NextState.Should().BeNull();
    }

    [Fact]
    public void Mixed_capacity_changes_are_confirmed_instead_of_immediate_risk_reduction()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1000m, 10m);
        var stability = new RecommendationStabilityPolicy();

        var valueIncreaseQuantityDecrease = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1200m, 8m),
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));
        var valueDecreaseQuantityIncrease = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 800m, 12m),
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));
        valueIncreaseQuantityDecrease.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        valueDecreaseQuantityIncrease.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
    }

    [Fact]
    public void Quantity_only_capacity_decrease_is_published_immediately()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1000m, 10m);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1000m, 8m),
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
    }

    [Fact]
    public void Quantity_only_capacity_increase_requires_add_allowed_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1000m, 8m);
        var stability = new RecommendationStabilityPolicy();

        var first = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1000m, 10m),
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));
        var second = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(6), 1000m, 10m),
            first.NextState,
            policy.StabilityProfile,
            T0.AddMinutes(6));
        var third = stability.Evaluate(
            current,
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(7), 1000m, 10m),
            second.NextState,
            policy.StabilityProfile,
            T0.AddMinutes(7));

        first.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        first.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
        second.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        second.NextState!.ConsecutiveObservations.Should().Be(2);
        third.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        third.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
    }

    [Fact]
    public void Nullable_quantity_capacity_follows_continuation_reduction_semantics()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var stability = new RecommendationStabilityPolicy();
        var knownQuantity = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1000m, 10m);
        var unknownQuantity = CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1000m, null);

        var reduction = stability.Evaluate(
            knownQuantity,
            unknownQuantity,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        var noDirectionalIncrease = stability.Evaluate(
            Recommendation.Create(
                assessment,
                CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(3), 1000m, null)),
            CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1000m, 10m),
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        reduction.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        reduction.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
        noDirectionalIncrease.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        noDirectionalIncrease.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
    }

    [Fact]
    public void Inherited_portfolio_reasons_are_part_of_candidate_semantics()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var currentAssessment = CreateAssessment(
            policy,
            positionId,
            portfolioDecision: RiskIncreaseDecision.Blocked,
            portfolioRiskReasons: [ReasonCode.PortfolioDataStale]);
        var candidateAssessment = CreateAssessment(
            policy,
            positionId,
            portfolioDecision: RiskIncreaseDecision.Blocked,
            portfolioRiskReasons: [ReasonCode.InsufficientFreeCapital]);
        var current = CreateRecommendation(currentAssessment, policy, T0.AddMinutes(3));
        var candidate = policy.Evaluate(candidateAssessment, policy, T0.AddMinutes(4));

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
        result.Reason.Should().NotBe(RecommendationStabilityReason.Duplicate);
        RecommendationSemanticState.From(candidate)
            .Should()
            .Be(RecommendationSemanticState.From(Recommendation.Create(candidateAssessment, candidate)));
    }

    [Fact]
    public void Watch_to_hold_requires_confirmation_with_the_same_policy()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(4));

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
    }

    [Fact]
    public void Watch_to_hold_requires_confirmation_when_policy_changes()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var candidate = WithIdentity(
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(4)),
            "recommendation-v2");

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
    }

    [Fact]
    public void Watch_to_hold_requires_confirmation_when_candidate_is_critical()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var candidate = WithActionPriority(
            policy.Evaluate(
                CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
                policy,
                T0.AddMinutes(4)),
            RecommendationPriority.Critical);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
    }

    [Fact]
    public void Watch_to_hold_with_policy_change_and_critical_priority_requires_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Watch, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var candidate = WithIdentity(
            WithActionPriority(
                policy.Evaluate(
                    CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
                    policy,
                    T0.AddMinutes(4)),
                RecommendationPriority.Critical),
            "recommendation-v2");

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AwaitingConfirmation);
    }

    [Theory]
    [InlineData(PositionAction.Hold, PositionAction.ProtectProfit)]
    [InlineData(PositionAction.Hold, PositionAction.MoveStop)]
    [InlineData(PositionAction.Hold, PositionAction.TakePartialProfit)]
    [InlineData(PositionAction.Watch, PositionAction.ProtectProfit)]
    [InlineData(PositionAction.Watch, PositionAction.MoveStop)]
    [InlineData(PositionAction.Watch, PositionAction.TakePartialProfit)]
    public void Stronger_protective_action_is_published_immediately(
        PositionAction currentAction,
        PositionAction candidateAction)
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(
                policy,
                positionId,
                currentAction,
                currentAction == PositionAction.Watch
                    ? RiskIncreaseDecision.Blocked
                    : RiskIncreaseDecision.Allowed),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(
                policy,
                positionId,
                candidateAction,
                RiskIncreaseDecision.Allowed),
            policy,
            T0.AddMinutes(4));

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
    }

    [Theory]
    [InlineData(PositionAction.ProtectProfit, PositionAction.MoveStop)]
    [InlineData(PositionAction.ProtectProfit, PositionAction.TakePartialProfit)]
    [InlineData(PositionAction.MoveStop, PositionAction.TakePartialProfit)]
    [InlineData(PositionAction.TakePartialProfit, PositionAction.Reduce)]
    [InlineData(PositionAction.Reduce, PositionAction.Close)]
    public void Protective_precedence_chain_is_published_immediately(
        PositionAction currentAction,
        PositionAction candidateAction)
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, currentAction),
            policy,
            T0.AddMinutes(3));
        var candidate = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, candidateAction),
            policy,
            T0.AddMinutes(4));

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
    }

    [Fact]
    public void Protective_action_does_not_bypass_add_permission_grant_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var current = CreateRecommendation(
            CreateAssessmentForAction(policy, positionId, PositionAction.Hold, RiskIncreaseDecision.Blocked),
            policy,
            T0.AddMinutes(3));
        var protectedEvaluation = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.ProtectProfit),
            policy,
            T0.AddMinutes(4));
        var addEvaluation = policy.Evaluate(
            CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above),
            policy,
            T0.AddMinutes(4));
        var candidate = CombineActionAndAdd(protectedEvaluation, addEvaluation);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
    }

    [Fact]
    public void Protective_action_does_not_bypass_capacity_increase_confirmation()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 800m, 8m);
        var protectedEvaluation = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.ProtectProfit),
            policy,
            T0.AddMinutes(4));
        var capacityEvaluation = CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 1200m, 12m);
        var candidate = CombineActionAndAdd(protectedEvaluation, capacityEvaluation);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PendingConfirmation);
        result.Reason.Should().Be(RecommendationStabilityReason.AddPermissionGranted);
    }

    [Fact]
    public void Protective_action_and_capacity_decrease_are_published_immediately()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(policy, positionId, stopPosition: AssessmentPricePosition.Above);
        var current = CreateCapacityRecommendation(assessment, policy, T0.AddMinutes(3), 1200m, 12m);
        var protectedEvaluation = policy.Evaluate(
            CreateAssessmentForAction(policy, positionId, PositionAction.ProtectProfit),
            policy,
            T0.AddMinutes(4));
        var capacityEvaluation = CreateCapacityEvaluation(assessment, policy, T0.AddMinutes(4), 800m, 8m);
        var candidate = CombineActionAndAdd(protectedEvaluation, capacityEvaluation);

        var result = new RecommendationStabilityPolicy().Evaluate(
            current,
            candidate,
            null,
            policy.StabilityProfile,
            T0.AddMinutes(4));

        result.Kind.Should().Be(RecommendationStabilityDecisionKind.PublishCandidate);
        result.Reason.Should().Be(RecommendationStabilityReason.RiskReduction);
    }

    [Fact]
    public void Evaluation_rejects_non_portfolio_inherited_reasons()
    {
        var policy = PolicyDefinition.Default;
        var positionId = PositionId.New();
        var assessment = CreateAssessment(
            policy,
            positionId,
            portfolioDecision: RiskIncreaseDecision.Blocked);
        var evaluation = policy.Evaluate(assessment, policy, T0.AddMinutes(4));

        FluentActions.Invoking(() => new RecommendationPolicyEvaluation(
                evaluation.PolicyIdentity,
                evaluation.Action,
                evaluation.AddDecision,
                evaluation.ContinuationPlan,
                evaluation.CreatedAt,
                evaluation.ValidUntil,
                [ReasonCode.TrendAligned]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RecommendationPolicyEvaluation(
                evaluation.PolicyIdentity,
                evaluation.Action,
                evaluation.AddDecision,
                evaluation.ContinuationPlan,
                evaluation.CreatedAt,
                evaluation.ValidUntil,
                [ReasonCode.PortfolioDataStale, ReasonCode.PortfolioDataStale]))
            .Should().Throw<ArgumentException>();
    }

    private static RecommendationStabilityProfile ConfirmationProfile() =>
        new(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1),
            3,
            TimeSpan.FromMinutes(2),
            2);

    private static Recommendation CreateRecommendation(
        PositionAssessment assessment,
        PolicyDefinition policy,
        DateTimeOffset createdAt)
    {
        var evaluation = policy.Evaluate(assessment, policy, createdAt);
        return Recommendation.Create(assessment, evaluation);
    }

    private static Recommendation CreateCapacityRecommendation(
        PositionAssessment assessment,
        PolicyDefinition policy,
        DateTimeOffset createdAt,
        decimal value,
        decimal? quantity) =>
        Recommendation.Create(assessment, CreateCapacityEvaluation(assessment, policy, createdAt, value, quantity));

    private static RecommendationPolicyEvaluation CreateCapacityEvaluation(
        PositionAssessment assessment,
        PolicyDefinition policy,
        DateTimeOffset createdAt,
        decimal value,
        decimal? quantity)
    {
        var evaluation = policy.Evaluate(assessment, policy, createdAt);
        var addDecision = new AddDecisionResult(
            AddDecision.AddAllowed,
            evaluation.AddDecision.ReasonCodes,
            value,
            quantity,
            evaluation.AddDecision.Conditions);
        return new(
            evaluation.PolicyIdentity,
            evaluation.Action,
            addDecision,
            evaluation.ContinuationPlan,
            evaluation.CreatedAt,
            evaluation.ValidUntil,
            evaluation.InheritedReasonCodes);
    }

    private static RecommendationPolicyEvaluation WithActionPriority(
        RecommendationPolicyEvaluation evaluation,
        RecommendationPriority priority) =>
        new(
            evaluation.PolicyIdentity,
            new RecommendedActionDecision(
                evaluation.Action.Action,
                evaluation.Action.Confidence,
                priority,
                evaluation.Action.ReasonCodes),
            evaluation.AddDecision,
            evaluation.ContinuationPlan,
            evaluation.CreatedAt,
            evaluation.ValidUntil,
            evaluation.InheritedReasonCodes);

    private static RecommendationPolicyEvaluation WithIdentity(
        RecommendationPolicyEvaluation evaluation,
        string version) =>
        new(
            PolicyConfigurationIdentity.From(version, new string('A', 64)),
            evaluation.Action,
            evaluation.AddDecision,
            evaluation.ContinuationPlan,
            evaluation.CreatedAt,
            evaluation.ValidUntil,
            evaluation.InheritedReasonCodes);

    private static RecommendationPolicyEvaluation CombineActionAndAdd(
        RecommendationPolicyEvaluation actionEvaluation,
        RecommendationPolicyEvaluation addEvaluation) =>
        new(
            actionEvaluation.PolicyIdentity,
            actionEvaluation.Action,
            addEvaluation.AddDecision,
            actionEvaluation.ContinuationPlan,
            actionEvaluation.CreatedAt,
            actionEvaluation.ValidUntil,
            actionEvaluation.InheritedReasonCodes);

    private static PositionAssessment CreateAssessmentForAction(
        PolicyDefinition policy,
        PositionId positionId,
        PositionAction action,
        RiskIncreaseDecision portfolioDecision = RiskIncreaseDecision.Allowed) =>
        action switch
        {
            PositionAction.Hold => CreateAssessment(
                policy,
                positionId,
                portfolioDecision: portfolioDecision),
            PositionAction.Watch => CreateAssessment(
                policy,
                positionId,
                alignment: PositionTrendAlignment.FlatOrUnknown,
                portfolioDecision: portfolioDecision),
            PositionAction.Reduce => CreateAssessment(
                policy,
                positionId,
                alignment: PositionTrendAlignment.Adverse,
                pnlPercent: -6m,
                portfolioDecision: portfolioDecision),
            PositionAction.Close => CreateAssessment(
                policy,
                positionId,
                alignment: PositionTrendAlignment.Adverse,
                pnlPercent: -12m,
                portfolioDecision: portfolioDecision),
            PositionAction.ProtectProfit => CreateAssessment(
                policy,
                positionId,
                pnlPercent: 3m,
                stopMissing: true,
                portfolioDecision: portfolioDecision),
            PositionAction.MoveStop => CreateAssessment(
                policy,
                positionId,
                pnlPercent: 3m,
                portfolioDecision: portfolioDecision),
            PositionAction.TakePartialProfit => CreateAssessment(
                policy,
                positionId,
                pnlPercent: 6m,
                stopPosition: AssessmentPricePosition.Above,
                exhaustion: true,
                resistanceNearby: true,
                portfolioDecision: portfolioDecision),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Test action is not configured.")
        };

    private static PositionAssessment CreateAssessment(
        PolicyDefinition policy,
        PositionId positionId,
        PositionTrendAlignment alignment = PositionTrendAlignment.Aligned,
        AssessmentDataQuality quality = AssessmentDataQuality.FreshCompleteReliable,
        RiskIncreaseDecision portfolioDecision = RiskIncreaseDecision.Allowed,
        decimal pnlPercent = 1m,
        bool stopMissing = false,
        AssessmentPricePosition stopPosition = AssessmentPricePosition.Below,
        AssessmentLiquidationState liquidationState = AssessmentLiquidationState.Far,
        decimal? liquidationDistance = 61m,
        bool exhaustion = false,
        bool resistanceNearby = false,
        IEnumerable<ReasonCode>? portfolioRiskReasons = null)
    {
        var inputVersions = new PositionAssessmentInputVersions(
            positionId,
            ExchangeAccountId.New(),
            InstrumentId.From("BTCUSDT"),
            T0,
            T0.AddMinutes(1),
            T0.AddMinutes(2),
            policy.Identity,
            policy.Identity);
        var result = new PositionAssessmentResult(
            PositionSide.Long,
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
                8_000m,
                1_000m,
                10m,
                10m,
                100m,
                25m),
            new(quality, quality));

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
            liquidationState == AssessmentLiquidationState.Far
                ? ReasonCode.LiquidationFar
                : ReasonCode.LiquidationNearby
        };
        if (quality != AssessmentDataQuality.FreshCompleteReliable)
            reasons.Add(ReasonCode.MarketDataUncertain);
        if (exhaustion)
            reasons.Add(ReasonCode.MomentumExhaustion);
        if (resistanceNearby)
            reasons.Add(ReasonCode.ResistanceNearby);

        return PositionAssessment.Create(
            inputVersions,
            new RuleVersion("assessment-v1"),
            portfolioDecision == RiskIncreaseDecision.Allowed
                ? RiskIncreasePolicyResult.Allowed()
                : RiskIncreasePolicyResult.Blocked(
                    portfolioRiskReasons ?? [ReasonCode.PortfolioDataStale]),
            result,
            reasons,
            T0.AddMinutes(2),
            T0.AddHours(1));
    }
}

internal static class RecommendationStabilityTestExtensions
{
    public static RecommendationPolicyEvaluation Evaluate(
        this PolicyDefinition policy,
        PositionAssessment assessment,
        PolicyDefinition policyDefinition,
        DateTimeOffset asOf) =>
        new RecommendationPolicy().Evaluate(assessment, policyDefinition, asOf);
}
