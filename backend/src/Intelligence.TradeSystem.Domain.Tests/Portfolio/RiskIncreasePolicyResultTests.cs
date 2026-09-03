using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class RiskIncreasePolicyResultTests
{
    [Fact]
    public void Allowed_Result_Contains_Only_RiskWithinLimits()
    {
        var result = RiskIncreasePolicyResult.Allowed();

        result.Decision.Should().Be(RiskIncreaseDecision.Allowed);
        result.ReasonCodes.Should().Equal(ReasonCode.RiskWithinLimits);
    }

    [Fact]
    public void Blocked_Result_Requires_A_Blocking_Reason_And_Rejects_RiskWithinLimits()
    {
        FluentActions.Invoking(() => RiskIncreasePolicyResult.Blocked([]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => RiskIncreasePolicyResult.Blocked([ReasonCode.RiskWithinLimits]))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Blocked_Result_Rejects_Undefined_Reason_And_Deduplicates()
    {
        FluentActions.Invoking(() => RiskIncreasePolicyResult.Blocked([(ReasonCode)999]))
            .Should().Throw<ArgumentOutOfRangeException>();

        var result = RiskIncreasePolicyResult.Blocked(
            [ReasonCode.PortfolioDataStale, ReasonCode.PortfolioDataStale]);

        result.ReasonCodes.Should().Equal(ReasonCode.PortfolioDataStale);
        result.ReasonCodes.Should().NotBeAssignableTo<List<ReasonCode>>();
    }

    [Fact]
    public void Allowed_Result_Cannot_Contain_Blocking_Reasons()
    {
        var result = RiskIncreasePolicyResult.Allowed();

        result.ReasonCodes.Should().NotContain(ReasonCode.PortfolioDataIncomplete);
    }

    [Fact]
    public void ReasonCodes_Are_Actually_ReadOnly()
    {
        var result = RiskIncreasePolicyResult.Blocked([ReasonCode.PortfolioDataStale]);

        result.ReasonCodes.Should().NotBeAssignableTo<List<ReasonCode>>();
        result.ReasonCodes.Should().BeAssignableTo<IReadOnlyList<ReasonCode>>();
    }
}
