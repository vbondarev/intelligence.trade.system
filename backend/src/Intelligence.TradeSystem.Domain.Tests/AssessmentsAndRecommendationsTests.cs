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
            reasons, Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1));
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
        PositionAssessment.Create(
            Inputs, new RuleVersion("v1"), result, [],
            Inputs.MarketCapturedAt, Inputs.MarketCapturedAt.AddHours(1));

    private static Recommendation CreateRecommendation(PositionAssessment assessment, AddDecision addDecision) =>
        Recommendation.Create(
            assessment, PositionAction.Hold, addDecision, new RuleVersion("policy-v1"),
            [ReasonCode.RiskWithinLimits], assessment.CreatedAt.AddMinutes(1),
            assessment.ValidUntil.AddMinutes(-1));
}
