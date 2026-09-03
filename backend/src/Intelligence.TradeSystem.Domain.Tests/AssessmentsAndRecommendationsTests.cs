using Intelligence.TradeSystem.Domain.Assessments;
using Intelligence.TradeSystem.Domain.Decisions;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Recommendations;

namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class AssessmentsAndRecommendationsTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly PositionAssessmentInputVersions Inputs = new(
        PositionId.New(), ExchangeAccountId.New(), InstrumentId.From("BTCUSDT"),
        ObservedAt, ObservedAt.AddMinutes(1), ObservedAt.AddMinutes(2));

    [Fact]
    public void Typed_Ids_Reject_Empty_And_New_Ids_Are_NonEmpty()
    {
        PositionAssessmentId.New().Value.Should().NotBe(Guid.Empty);
        RecommendationId.New().Value.Should().NotBe(Guid.Empty);
        FluentActions.Invoking(() => PositionAssessmentId.FromGuid(Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RecommendationId.FromGuid(Guid.Empty)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuleVersion_Trims_And_Rejects_Invalid_Values()
    {
        new RuleVersion("  1.0  ").Value.Should().Be("1.0");
        new RuleVersion("1.0").Should().Be(new RuleVersion("1.0"));
        FluentActions.Invoking(() => new RuleVersion(null!)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new RuleVersion("  ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assessment_Copies_Inputs_And_Deduplicates_ReadOnly_Reasons()
    {
        var assessment = PositionAssessment.Create(
            Inputs, new RuleVersion("assessment-v1"), RiskIncreasePolicyResult.Allowed(),
            [], Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1));

        assessment.PositionId.Should().Be(Inputs.PositionId);
        assessment.RuleVersion.Value.Should().Be("assessment-v1");
        assessment.ReasonCodes.Should().Equal(ReasonCode.RiskWithinLimits);
        assessment.ReasonCodes.Should().NotBeAssignableTo<List<ReasonCode>>();
        assessment.IsValidAt(assessment.CreatedAt).Should().BeTrue();
        assessment.IsValidAt(assessment.ValidUntil).Should().BeFalse();
    }

    [Fact]
    public void Decision_Vocabularies_Are_Explicit_And_Separate()
    {
        Enum.GetValues<PositionAction>().Should().Equal(
            PositionAction.Hold, PositionAction.Watch, PositionAction.ProtectProfit,
            PositionAction.Reduce, PositionAction.Close, PositionAction.MoveStop,
            PositionAction.TakePartialProfit);
        Enum.GetValues<AddDecision>().Should().Equal(
            AddDecision.NotEvaluated, AddDecision.DoNotAdd, AddDecision.AddAllowed);
        Enum.GetValues<RiskIncreaseDecision>().Should().Equal(
            RiskIncreaseDecision.Allowed, RiskIncreaseDecision.Blocked);
    }

    [Fact]
    public void Typed_Ids_And_Created_Entities_Are_Unique()
    {
        PositionAssessmentId.New().Should().NotBe(PositionAssessmentId.New());
        RecommendationId.New().Should().NotBe(RecommendationId.New());

        var firstAssessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var secondAssessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        firstAssessment.Id.Should().NotBe(secondAssessment.Id);

        var firstRecommendation = CreateRecommendation(firstAssessment, AddDecision.NotEvaluated);
        var secondRecommendation = CreateRecommendation(firstAssessment, AddDecision.NotEvaluated);
        firstRecommendation.Id.Should().NotBe(secondRecommendation.Id);
    }

    [Fact]
    public void Assessment_Timestamp_Boundaries_Are_Explicit()
    {
        FluentActions.Invoking(() => CreateAssessmentAt(Inputs.PositionObservedAt.AddTicks(-1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.PortfolioCalculatedAt.AddTicks(-1), Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.MarketCapturedAt.AddTicks(-1), Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();

        var assessment = CreateAssessmentAt(Inputs.MarketCapturedAt);
        assessment.CreatedAt.Should().Be(Inputs.MarketCapturedAt);
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            assessment.CreatedAt, assessment.CreatedAt))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            assessment.CreatedAt, assessment.CreatedAt.AddTicks(-1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assessment_IsValidAt_Covers_All_Boundaries()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        assessment.IsValidAt(assessment.CreatedAt).Should().BeTrue();
        assessment.IsValidAt(assessment.CreatedAt.AddTicks(1)).Should().BeTrue();
        assessment.IsValidAt(assessment.CreatedAt.AddTicks(-1)).Should().BeFalse();
        assessment.IsValidAt(assessment.ValidUntil).Should().BeFalse();
        assessment.IsValidAt(assessment.ValidUntil.AddTicks(1)).Should().BeFalse();
    }

    [Fact]
    public void Assessment_Rejects_Invalid_Chronology_And_Reasons()
    {
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.PositionObservedAt.AddMinutes(-1), Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [(ReasonCode)999],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Default_Input_Versions_Are_Rejected_At_Assessment_Boundary()
    {
        FluentActions.Invoking(() => PositionAssessment.Create(
            default, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Individual_Default_Input_Identities_Are_Rejected()
    {
        FluentActions.Invoking(() => PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                default, Inputs.ExchangeAccountId, Inputs.InstrumentId,
                Inputs.PositionObservedAt, Inputs.PortfolioCalculatedAt, Inputs.MarketCapturedAt),
            new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                Inputs.PositionId, default, Inputs.InstrumentId,
                Inputs.PositionObservedAt, Inputs.PortfolioCalculatedAt, Inputs.MarketCapturedAt),
            new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => PositionAssessment.Create(
            new PositionAssessmentInputVersions(
                Inputs.PositionId, Inputs.ExchangeAccountId, default,
                Inputs.PositionObservedAt, Inputs.PortfolioCalculatedAt, Inputs.MarketCapturedAt),
            new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assessment_Cannot_Add_Portfolio_Risk_Reasons()
    {
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(),
            [ReasonCode.PortfolioDataStale], Inputs.MarketCapturedAt,
            Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();

        var blocked = RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]);
        FluentActions.Invoking(() => PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), blocked, [ReasonCode.RiskWithinLimits],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recommendation_Enforces_Add_Safety_And_Validity()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]));
        FluentActions.Invoking(() => CreateRecommendation(assessment, AddDecision.AddAllowed))
            .Should().Throw<InvalidOperationException>();

        var allowed = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var recommendation = CreateRecommendation(allowed, AddDecision.NotEvaluated);
        recommendation.Status.Should().Be(RecommendationStatus.Active);
        recommendation.AssessmentId.Should().Be(allowed.Id);
        recommendation.PositionId.Should().Be(allowed.PositionId);
    }

    [Fact]
    public void Recommendation_Lifecycle_Is_Chronological_And_Idempotent()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var recommendation = CreateRecommendation(assessment, AddDecision.DoNotAdd);
        var acknowledgedAt = recommendation.CreatedAt.AddMinutes(1);
        recommendation.Acknowledge(acknowledgedAt);
        recommendation.Acknowledge(recommendation.CreatedAt);
        recommendation.AcknowledgedAt.Should().Be(acknowledgedAt);

        recommendation.Dismiss(acknowledgedAt.AddMinutes(1));
        recommendation.Dismiss(acknowledgedAt);
        recommendation.Status.Should().Be(RecommendationStatus.Dismissed);
        recommendation.IsEffectiveAt(recommendation.ValidUntil).Should().BeFalse();
    }

    [Fact]
    public void Recommendation_Inherits_Assessment_Portfolio_Reasons()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]));
        var recommendation = CreateRecommendation(assessment, AddDecision.NotEvaluated);

        recommendation.ReasonCodes.Should().ContainSingle().Which.Should().Be(ReasonCode.PortfolioDataStale);
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, PositionAction.Hold, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [ReasonCode.RiskWithinLimits], assessment.CreatedAt.AddMinutes(1),
            assessment.ValidUntil.AddMinutes(-1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Recommendation_Creation_Boundaries_Are_Explicit()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        FluentActions.Invoking(() => CreateRecommendation(
            assessment, AddDecision.NotEvaluated, assessment.CreatedAt.AddTicks(-1)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CreateRecommendation(
            assessment, AddDecision.NotEvaluated, assessment.ValidUntil))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, PositionAction.Hold, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [], assessment.CreatedAt.AddMinutes(1), assessment.CreatedAt.AddMinutes(1)))
            .Should().Throw<ArgumentException>();
        Recommendation.Create(
            assessment, PositionAction.Hold, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [], assessment.CreatedAt.AddMinutes(1), assessment.ValidUntil)
            .ValidUntil.Should().Be(assessment.ValidUntil);
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, PositionAction.Hold, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [], assessment.CreatedAt.AddMinutes(1), assessment.ValidUntil.AddTicks(1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Undefined_Recommendation_Values_Are_Rejected()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, (PositionAction)999, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [], assessment.CreatedAt.AddMinutes(1), assessment.ValidUntil))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, PositionAction.Hold, (AddDecision)999, new RuleVersion("v1"),
            [], assessment.CreatedAt.AddMinutes(1), assessment.ValidUntil))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Recommendation.Create(
            assessment, PositionAction.Hold, AddDecision.NotEvaluated, new RuleVersion("v1"),
            [(ReasonCode)999], assessment.CreatedAt.AddMinutes(1), assessment.ValidUntil))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Acknowledged_Recommendation_Cannot_Be_Dismissed_Before_AcknowledgedAt()
    {
        var recommendation = CreateRecommendation(
            CreateAssessment(RiskIncreasePolicyResult.Allowed()), AddDecision.NotEvaluated);
        var acknowledgedAt = recommendation.CreatedAt.AddMinutes(2);
        recommendation.Acknowledge(acknowledgedAt);

        FluentActions.Invoking(() => recommendation.Dismiss(recommendation.CreatedAt.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
        var dismissedAgain = FluentActions.Invoking(
            () => recommendation.Dismiss(recommendation.CreatedAt.AddMinutes(1)));
        dismissedAgain.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Dismiss");
        recommendation.Dismiss(acknowledgedAt);
        recommendation.Dismiss(recommendation.ValidUntil.AddHours(1));
        recommendation.DismissedAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void Supersede_Requires_Newer_Same_Position_Successor_And_Is_Idempotent()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var current = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        var successor = CreateRecommendation(
            assessment, AddDecision.DoNotAdd, current.CreatedAt.AddMinutes(1));

        current.SupersedeBy(successor);
        current.SupersedeBy(successor);
        current.Status.Should().Be(RecommendationStatus.Superseded);
        current.SupersededAt.Should().Be(successor.CreatedAt);
        current.SupersededByRecommendationId.Should().Be(successor.Id);

        var otherSuccessor = CreateRecommendation(
            assessment, AddDecision.NotEvaluated, successor.CreatedAt.AddMinutes(1));
        FluentActions.Invoking(() => current.SupersedeBy(otherSuccessor))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Supersede_Rejects_Self_Older_Different_Position_And_Validity_Boundaries()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var current = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        FluentActions.Invoking(() => current.SupersedeBy(current))
            .Should().Throw<InvalidOperationException>();

        var older = CreateRecommendation(assessment, AddDecision.NotEvaluated, current.CreatedAt);
        FluentActions.Invoking(() => current.SupersedeBy(older))
            .Should().Throw<InvalidOperationException>();

        var differentInputs = new PositionAssessmentInputVersions(
            PositionId.New(), Inputs.ExchangeAccountId, Inputs.InstrumentId,
            Inputs.PositionObservedAt, Inputs.PortfolioCalculatedAt, Inputs.MarketCapturedAt);
        var differentAssessment = CreateAssessment(
            differentInputs, RiskIncreasePolicyResult.Allowed());
        var differentPosition = CreateRecommendation(differentAssessment, AddDecision.NotEvaluated);
        FluentActions.Invoking(() => current.SupersedeBy(differentPosition))
            .Should().Throw<InvalidOperationException>();

        var atBoundary = CreateRecommendation(
            assessment, AddDecision.NotEvaluated, current.ValidUntil, assessment.ValidUntil);
        FluentActions.Invoking(() => current.SupersedeBy(atBoundary))
            .Should().Throw<InvalidOperationException>();
        var afterBoundary = CreateRecommendation(
            assessment, AddDecision.NotEvaluated, current.ValidUntil.AddTicks(1), assessment.ValidUntil);
        FluentActions.Invoking(() => current.SupersedeBy(afterBoundary))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Supersede_Requires_Successor_Not_Before_Acknowledgement()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var current = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        var acknowledgedAt = current.CreatedAt.AddMinutes(5);
        current.Acknowledge(acknowledgedAt);

        var beforeAcknowledgement = CreateRecommendation(
            assessment, AddDecision.NotEvaluated, current.CreatedAt.AddMinutes(2));
        FluentActions.Invoking(() => current.SupersedeBy(beforeAcknowledgement))
            .Should().Throw<InvalidOperationException>();

        var atAcknowledgement = CreateRecommendation(
            assessment, AddDecision.NotEvaluated, acknowledgedAt);
        current.SupersedeBy(atAcknowledgement);
        current.SupersededAt.Should().Be(acknowledgedAt);
    }

    [Fact]
    public void Terminal_Recommendations_Reject_New_Transitions()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var dismissed = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        dismissed.Dismiss(dismissed.CreatedAt);
        FluentActions.Invoking(() => dismissed.Acknowledge(dismissed.CreatedAt)).Should().Throw<InvalidOperationException>();

        var expired = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        expired.ExpireIfDue(expired.ValidUntil);
        FluentActions.Invoking(() => expired.Acknowledge(expired.CreatedAt)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => expired.Dismiss(expired.CreatedAt)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => expired.SupersedeBy(
            CreateRecommendation(assessment, AddDecision.NotEvaluated, expired.CreatedAt.AddMinutes(1))))
            .Should().Throw<InvalidOperationException>();

        var superseded = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        var successor = CreateRecommendation(assessment, AddDecision.NotEvaluated, superseded.CreatedAt.AddMinutes(1));
        superseded.SupersedeBy(successor);
        FluentActions.Invoking(() => superseded.Acknowledge(superseded.CreatedAt))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => superseded.Dismiss(superseded.CreatedAt)).Should().Throw<InvalidOperationException>();
        var acknowledgeSuperseded = FluentActions.Invoking(
            () => superseded.Acknowledge(superseded.CreatedAt));
        acknowledgeSuperseded.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Acknowledge");
    }

    [Fact]
    public void Recommendation_Expires_At_Boundary_And_Is_Not_Effective()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var recommendation = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        recommendation.ExpireIfDue(recommendation.ValidUntil);

        recommendation.Status.Should().Be(RecommendationStatus.Expired);
        recommendation.ExpiredAt.Should().Be(recommendation.ValidUntil);
        recommendation.ExpireIfDue(recommendation.ValidUntil.AddMinutes(1));
        recommendation.ExpiredAt.Should().Be(recommendation.ValidUntil);
        recommendation.IsEffectiveAt(recommendation.ValidUntil).Should().BeFalse();
    }

    [Fact]
    public void Acknowledged_Recommendation_Expires_At_Validity_Boundary()
    {
        var recommendation = CreateRecommendation(
            CreateAssessment(RiskIncreasePolicyResult.Allowed()), AddDecision.NotEvaluated);
        recommendation.Acknowledge(recommendation.CreatedAt);
        recommendation.ExpireIfDue(recommendation.ValidUntil);

        recommendation.Status.Should().Be(RecommendationStatus.Expired);
        recommendation.ExpiredAt.Should().Be(recommendation.ValidUntil);
    }

    [Fact]
    public void Recommendations_Do_Not_Expire_Before_Due_And_Terminal_Expiry_Is_Idempotent()
    {
        var active = CreateRecommendation(
            CreateAssessment(RiskIncreasePolicyResult.Allowed()), AddDecision.NotEvaluated);
        active.ExpireIfDue(active.ValidUntil.AddTicks(-1));
        active.Status.Should().Be(RecommendationStatus.Active);
        active.ExpiredAt.Should().BeNull();

        var dismissed = CreateRecommendation(
            CreateAssessment(RiskIncreasePolicyResult.Allowed()), AddDecision.NotEvaluated);
        dismissed.Dismiss(dismissed.CreatedAt);
        dismissed.ExpireIfDue(dismissed.ValidUntil.AddMinutes(1));
        dismissed.Status.Should().Be(RecommendationStatus.Dismissed);
        dismissed.ExpiredAt.Should().BeNull();

        var supersededAssessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var superseded = CreateRecommendation(supersededAssessment, AddDecision.NotEvaluated);
        var successor = CreateRecommendation(
            supersededAssessment, AddDecision.NotEvaluated, superseded.CreatedAt.AddMinutes(1));
        superseded.SupersedeBy(successor);
        superseded.ExpireIfDue(superseded.ValidUntil.AddMinutes(1));
        superseded.Status.Should().Be(RecommendationStatus.Superseded);
        superseded.ExpiredAt.Should().BeNull();
    }

    [Fact]
    public void Recommendation_IsEffectiveAt_Uses_Current_Status_Not_History()
    {
        var assessment = CreateAssessment(RiskIncreasePolicyResult.Allowed());
        var active = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        active.IsEffectiveAt(active.CreatedAt).Should().BeTrue();
        active.IsEffectiveAt(active.CreatedAt.AddTicks(-1)).Should().BeFalse();
        active.IsEffectiveAt(active.ValidUntil).Should().BeFalse();
        active.IsEffectiveAt(active.ValidUntil.AddTicks(1)).Should().BeFalse();

        var acknowledged = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        acknowledged.Acknowledge(acknowledged.CreatedAt);
        acknowledged.IsEffectiveAt(acknowledged.CreatedAt).Should().BeTrue();

        var dismissed = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        dismissed.Dismiss(dismissed.CreatedAt);
        dismissed.IsEffectiveAt(dismissed.CreatedAt).Should().BeFalse();
        dismissed.IsEffectiveAt(dismissed.CreatedAt.AddTicks(-1)).Should().BeFalse();

        var superseded = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        var successor = CreateRecommendation(assessment, AddDecision.NotEvaluated, superseded.CreatedAt.AddMinutes(1));
        superseded.SupersedeBy(successor);
        superseded.IsEffectiveAt(superseded.CreatedAt).Should().BeFalse();

        var expired = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        expired.ExpireIfDue(expired.ValidUntil);
        expired.IsEffectiveAt(expired.CreatedAt).Should().BeFalse();
    }

    private static PositionAssessment CreateAssessment(RiskIncreasePolicyResult result) =>
        CreateAssessment(Inputs, result);

    private static PositionAssessment CreateAssessmentAt(DateTimeOffset createdAt) =>
        PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), RiskIncreasePolicyResult.Allowed(), [],
            createdAt, Inputs.MarketCapturedAt.AddHours(1));

    private static PositionAssessment CreateAssessment(
        PositionAssessmentInputVersions inputVersions, RiskIncreasePolicyResult result) =>
        PositionAssessment.Create(
            inputVersions, new RuleVersion("v1"), result, [],
            inputVersions.MarketCapturedAt, inputVersions.MarketCapturedAt.AddHours(1));

    private static Recommendation CreateRecommendation(PositionAssessment assessment, AddDecision addDecision) =>
        CreateRecommendation(assessment, addDecision, assessment.CreatedAt.AddMinutes(1));

    private static Recommendation CreateRecommendation(
        PositionAssessment assessment, AddDecision addDecision, DateTimeOffset createdAt) =>
        CreateRecommendation(assessment, addDecision, createdAt, assessment.ValidUntil.AddMinutes(-1));

    private static Recommendation CreateRecommendation(
        PositionAssessment assessment, AddDecision addDecision, DateTimeOffset createdAt, DateTimeOffset validUntil) =>
        Recommendation.Create(
            assessment, PositionAction.Hold, addDecision, new RuleVersion("policy-v1"),
            [], createdAt,
            validUntil);
}
