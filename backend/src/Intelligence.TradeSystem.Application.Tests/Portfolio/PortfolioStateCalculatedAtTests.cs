using Intelligence.TradeSystem.Application.Portfolio;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioStateCalculatedAtTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Sub_Microsecond_Candidate_Is_Rounded_Up_Not_Below_The_Observation(long ticks)
    {
        var observedAt = T0.AddTicks(ticks);

        var resolved = PortfolioStateCalculatedAt.Resolve(observedAt, previous: null);

        resolved.Should().Be(T0.AddTicks(TimeSpan.TicksPerMicrosecond));
        resolved.Should().BeOnOrAfter(observedAt);
        resolved.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Microsecond_Aligned_Candidate_Is_Kept()
    {
        PortfolioStateCalculatedAt.Resolve(T0.AddTicks(TimeSpan.TicksPerMicrosecond), previous: T0)
            .Should().Be(T0.AddTicks(TimeSpan.TicksPerMicrosecond));
    }

    [Fact]
    public void Candidate_With_Offset_Is_Normalized_To_Utc()
    {
        var local = new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.FromHours(3)).AddTicks(1);

        var resolved = PortfolioStateCalculatedAt.Resolve(local, previous: null);

        resolved.Offset.Should().Be(TimeSpan.Zero);
        resolved.Should().Be(T0.AddTicks(TimeSpan.TicksPerMicrosecond));
    }

    [Fact]
    public void Candidate_Equal_To_Previous_Advances_By_One_Microsecond()
    {
        PortfolioStateCalculatedAt.Resolve(T0, previous: T0)
            .Should().Be(T0.AddTicks(TimeSpan.TicksPerMicrosecond));
    }

    [Fact]
    public void Candidate_Rounded_Up_To_Previous_Advances_Beyond_It()
    {
        var previous = T0.AddTicks(TimeSpan.TicksPerMicrosecond);

        PortfolioStateCalculatedAt.Resolve(T0.AddTicks(1), previous)
            .Should().Be(T0.AddTicks(2 * TimeSpan.TicksPerMicrosecond));
    }

    [Fact]
    public void Clock_Rollback_Keeps_Snapshot_Order_And_Lower_Bound_Of_The_Observation()
    {
        var previous = T0.AddSeconds(10);
        var observedAt = T0.AddSeconds(20).AddTicks(1);

        var afterNewerObservation = PortfolioStateCalculatedAt.Resolve(observedAt, previous);
        var afterRollback = PortfolioStateCalculatedAt.Resolve(T0.AddSeconds(5), afterNewerObservation);

        afterNewerObservation.Should().Be(T0.AddSeconds(20).AddTicks(TimeSpan.TicksPerMicrosecond));
        afterNewerObservation.Should().BeAfter(observedAt);
        afterRollback.Should().Be(afterNewerObservation.AddTicks(TimeSpan.TicksPerMicrosecond));
    }

    [Fact]
    public void Previous_With_Sub_Microsecond_Precision_Is_Still_Strictly_Exceeded()
    {
        var previous = T0.AddTicks(3);

        var resolved = PortfolioStateCalculatedAt.Resolve(T0, previous);

        resolved.Should().BeAfter(previous);
        (resolved.UtcTicks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
    }

    [Fact]
    public void Candidate_Rounded_Beyond_The_Supported_Range_Throws()
    {
        var lastMicrosecond = new DateTimeOffset(
            DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.MaxValue.UtcTicks % TimeSpan.TicksPerMicrosecond,
            TimeSpan.Zero);

        PortfolioStateCalculatedAt.Resolve(lastMicrosecond, previous: null).Should().Be(lastMicrosecond);
        var rounding = () => PortfolioStateCalculatedAt.Resolve(DateTimeOffset.MaxValue, previous: null);
        var advancing = () => PortfolioStateCalculatedAt.Resolve(lastMicrosecond, lastMicrosecond);

        rounding.Should().Throw<InvalidOperationException>();
        advancing.Should().Throw<InvalidOperationException>();
    }
}
