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
        var reasons = new List<ReasonCode> { ReasonCode.RiskWithinLimits };
        var assessment = PositionAssessment.Create(
            Inputs, new RuleVersion("assessment-v1"), RiskIncreasePolicyResult.Allowed(),
            [], Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1));
        reasons.Add(ReasonCode.PortfolioDataStale);

        assessment.PositionId.Should().Be(Inputs.PositionId);
        assessment.RuleVersion.Value.Should().Be("assessment-v1");
        assessment.ReasonCodes.Should().Equal(ReasonCode.RiskWithinLimits);
        assessment.ReasonCodes.Should().NotBeAssignableTo<List<ReasonCode>>();
        assessment.IsValidAt(assessment.CreatedAt).Should().BeTrue();
        assessment.IsValidAt(assessment.ValidUntil).Should().BeFalse();
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
    public void Dismiss_Cannot_Precede_Acknowledgement_And_Is_Idempotent()
    {
        var recommendation = CreateRecommendation(
            CreateAssessment(RiskIncreasePolicyResult.Allowed()), AddDecision.NotEvaluated);
        var acknowledgedAt = recommendation.CreatedAt.AddMinutes(2);
        recommendation.Acknowledge(acknowledgedAt);

        FluentActions.Invoking(() => recommendation.Dismiss(recommendation.CreatedAt.AddMinutes(1)))
            .Should().Throw<InvalidOperationException>();
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

        var superseded = CreateRecommendation(assessment, AddDecision.NotEvaluated);
        var successor = CreateRecommendation(assessment, AddDecision.NotEvaluated, superseded.CreatedAt.AddMinutes(1));
        superseded.SupersedeBy(successor);
        FluentActions.Invoking(() => superseded.Dismiss(superseded.CreatedAt)).Should().Throw<InvalidOperationException>();
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

    private static PositionAssessment CreateAssessment(RiskIncreasePolicyResult result) =>
        CreateAssessment(Inputs, result);

    private static PositionAssessment CreateAssessment(
        PositionAssessmentInputVersions inputVersions, RiskIncreasePolicyResult result) =>
        PositionAssessment.Create(
            inputVersions, new RuleVersion("v1"), result, [],
            inputVersions.MarketCapturedAt, inputVersions.MarketCapturedAt.AddHours(1));

    private static Recommendation CreateRecommendation(PositionAssessment assessment, AddDecision addDecision) =>
        CreateRecommendation(assessment, addDecision, assessment.CreatedAt.AddMinutes(1));

    private static Recommendation CreateRecommendation(
        PositionAssessment assessment, AddDecision addDecision, DateTimeOffset createdAt) =>
        Recommendation.Create(
            assessment, PositionAction.Hold, addDecision, new RuleVersion("policy-v1"),
            [], createdAt,
            assessment.ValidUntil.AddMinutes(-1));
}
