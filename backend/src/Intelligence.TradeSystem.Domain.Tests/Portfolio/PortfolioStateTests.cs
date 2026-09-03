using Intelligence.TradeSystem.Domain.Portfolio;

namespace Intelligence.TradeSystem.Domain.Tests;

public sealed class PortfolioStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ExchangeAccountId Account = ExchangeAccountId.New();

    [Fact]
    public void Calculates_Exposure_Pnl_Capital_And_Concentration()
    {
        var longPosition = CreatePosition(PositionSide.Long, 75m, 10m);
        var shortPosition = CreatePosition(PositionSide.Short, 25m, -2m);
        var state = CreateState([longPosition, shortPosition]);

        state.GrossExposure.Should().Be(100m);
        state.LongExposure.Should().Be(75m);
        state.ShortExposure.Should().Be(25m);
        state.NetExposure.Should().Be(50m);
        state.TotalUnrealizedPnl.Should().Be(8m);
        state.UsedCapital.Should().Be(40m);
        state.FreeCapital.Should().Be(60m);
        state.FreeCapitalPercent.Should().Be(60m);
        state.GrossExposureToEquityPercent.Should().Be(100m);
        state.LargestPositionConcentrationPercent.Should().Be(75m);
        state.LargestPositionId.Should().Be(longPosition.Id);
        state.IsComplete.Should().BeTrue();
        state.IsFresh.Should().BeTrue();
    }

    [Fact]
    public void Excludes_Closed_But_Includes_Unknown_And_Stale()
    {
        var closed = CreatePosition(PositionSide.Long, 100m, 1m);
        closed.Close(T0.AddMinutes(1));
        var unknown = CreatePosition(PositionSide.Long, 20m, 1m);
        unknown.MarkUnknown(T0.AddMinutes(1));
        var state = CreateState([closed, unknown]);

        state.Positions.Should().ContainSingle();
        state.GrossExposure.Should().Be(20m);
        state.IsFresh.Should().BeFalse();
    }

    [Fact]
    public void Copies_Position_And_Exposes_ReadOnly_Collection()
    {
        var position = CreatePosition(PositionSide.Long, 10m, 1m);
        var state = CreateState([position]);
        position.ApplyObservation(2m, T0.AddMinutes(1), positionValue: 20m, unrealizedPnl: 2m);

        state.Positions[0].Size.Should().Be(1m);
        state.Positions.Should().BeAssignableTo<IReadOnlyList<PortfolioPositionState>>();
        state.Positions.Should().NotBeAssignableTo<List<PortfolioPositionState>>();
    }

    [Fact]
    public void Unknown_Numeric_Values_Make_Aggregates_Incomplete()
    {
        var unknownValue = CreatePosition(PositionSide.Long, null, null);
        var state = CreateState([unknownValue]);

        state.GrossExposure.Should().BeNull();
        state.TotalUnrealizedPnl.Should().BeNull();
        state.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Validates_Scope_Timestamps_And_Capital()
    {
        var otherAccountKey = ExchangePositionKey.Create(
            ExchangeAccountId.New(), InstrumentId.From("ETHUSDT"), PositionSide.Long, 0);
        var otherPosition = Position.Create(otherAccountKey, MarketCategory.Linear, 1m, T0, T0);

        FluentActions.Invoking(() => CreateState([otherPosition]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new PortfolioCapitalState(1m, 2m, T0))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CreateState([], staleAfter: TimeSpan.FromSeconds(-1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void No_Positions_Have_Zero_Exposure_And_Pnl()
    {
        var state = CreateState([]);

        state.GrossExposure.Should().Be(0m);
        state.LongExposure.Should().Be(0m);
        state.ShortExposure.Should().Be(0m);
        state.NetExposure.Should().Be(0m);
        state.TotalUnrealizedPnl.Should().Be(0m);
        state.LargestPositionConcentrationPercent.Should().Be(0m);
    }

    [Fact]
    public void Policy_Accumulates_Violations_And_Allows_At_Boundaries()
    {
        var state = CreateState(
            [CreatePosition(PositionSide.Long, 90m, 1m)],
            equity: 100m,
            available: 10m);
        var settings = new PortfolioRiskPolicySettings(10m, 90m, 100m);

        var result = PortfolioRiskPolicy.EvaluateRiskIncrease(state, settings);

        result.Decision.Should().Be(RiskIncreaseDecision.Allowed);
        result.ReasonCodes.Should().ContainSingle().Which.Should().Be(ReasonCode.RiskWithinLimits);
    }

    [Fact]
    public void Policy_Blocks_With_All_Applicable_Reasons()
    {
        var state = CreateState([CreatePosition(PositionSide.Long, 90m, 1m)], equity: 100m, available: 5m);
        var result = PortfolioRiskPolicy.EvaluateRiskIncrease(
            state, new PortfolioRiskPolicySettings(10m, 50m, 50m));

        result.Decision.Should().Be(RiskIncreaseDecision.Blocked);
        result.ReasonCodes.Should().BeEquivalentTo(
            [ReasonCode.InsufficientFreeCapital, ReasonCode.GrossExposureLimitExceeded,
             ReasonCode.ConcentrationLimitExceeded]);
    }

    private static PortfolioState CreateState(
        IEnumerable<Position> positions,
        decimal equity = 100m,
        decimal available = 60m,
        TimeSpan? staleAfter = null) =>
        PortfolioState.Create(
            Account,
            positions,
            new PortfolioCapitalState(equity, available, T0),
            T0.AddMinutes(1),
            staleAfter ?? TimeSpan.FromMinutes(5));

    private static Position CreatePosition(PositionSide side, decimal? value, decimal? pnl)
    {
        var key = ExchangePositionKey.Create(
            Account, InstrumentId.From(Guid.NewGuid().ToString("N")), side, 0);
        return Position.Create(
            key, MarketCategory.Linear, 1m, T0, T0,
            positionValue: value, unrealizedPnl: pnl, averageEntryPrice: 100m, leverage: 2m);
    }
}
