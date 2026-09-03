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
    public void CalculatedAt_Before_CapitalObservedAt_Is_Rejected()
    {
        FluentActions.Invoking(() => PortfolioState.Create(
                Account, [], new PortfolioCapitalState(100m, 50m, T0.AddMinutes(2)),
                T0.AddMinutes(1), TimeSpan.FromMinutes(5)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Missing_CapitalObservedAt_Makes_Portfolio_NotFresh()
    {
        var state = PortfolioState.Create(
            Account, [], new PortfolioCapitalState(100m, 50m, null), T0.AddMinutes(1), TimeSpan.FromMinutes(5));

        state.IsComplete.Should().BeTrue();
        state.IsFresh.Should().BeFalse();
    }

    [Fact]
    public void Zero_Equity_Makes_Portfolio_Incomplete_And_Blocks_RiskIncrease()
    {
        var state = CreateState([], equity: 0m, available: 0m);
        var result = PortfolioRiskPolicy.EvaluateRiskIncrease(
            state, new PortfolioRiskPolicySettings(0m, 0m, 0m));

        state.FreeCapitalPercent.Should().BeNull();
        state.GrossExposureToEquityPercent.Should().BeNull();
        state.IsComplete.Should().BeFalse();
        result.Decision.Should().Be(RiskIncreaseDecision.Blocked);
        result.ReasonCodes.Should().Contain(ReasonCode.PortfolioDataIncomplete);
        result.ReasonCodes.Should().NotContain(ReasonCode.RiskWithinLimits);
    }

    [Fact]
    public void Complete_When_Equity_Is_Positive_And_All_Required_Data_Is_Known()
    {
        var state = CreateState([CreatePosition(PositionSide.Long, 10m, 1m)]);

        state.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Missing_Equity_Or_AvailableCapital_Makes_Portfolio_Incomplete()
    {
        CreateState([], equity: null, available: 0m).IsComplete.Should().BeFalse();
        CreateState([], equity: 100m, available: null).IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Freshness_Is_Independent_From_Completeness()
    {
        var incomplete = CreateState([CreatePosition(PositionSide.Long, null, 1m)]);
        var stalePosition = CreatePosition(PositionSide.Long, 10m, 1m);
        stalePosition.RefreshFreshness(T0.AddMinutes(10), TimeSpan.FromMinutes(5));
        var stale = CreateState([stalePosition]);

        incomplete.IsFresh.Should().BeTrue();
        incomplete.IsComplete.Should().BeFalse();
        stale.IsFresh.Should().BeFalse();
        stale.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Exactly_At_Stale_Threshold_Is_Fresh_But_Older_Is_Not()
    {
        var exactly = PortfolioState.Create(
            Account, [], new PortfolioCapitalState(100m, 50m, T0),
            T0.AddMinutes(5), TimeSpan.FromMinutes(5));
        var older = PortfolioState.Create(
            Account, [], new PortfolioCapitalState(100m, 50m, T0),
            T0.AddMinutes(5).AddTicks(1), TimeSpan.FromMinutes(5));

        exactly.IsFresh.Should().BeTrue();
        older.IsFresh.Should().BeFalse();
    }

    [Fact]
    public void CalculatedAt_Before_PositionLastObservedAt_Is_Rejected()
    {
        var position = CreatePosition(PositionSide.Long, 10m, 1m);

        FluentActions.Invoking(() => PortfolioState.Create(
                Account, [position], new PortfolioCapitalState(100m, 50m, T0),
                T0.AddTicks(-1), TimeSpan.FromMinutes(5)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GrossExposure_To_Equity_Can_Exceed_One_Hundred_Percent()
    {
        var state = CreateState([CreatePosition(PositionSide.Long, 250m, 1m)]);

        state.GrossExposureToEquityPercent.Should().Be(250m);
    }

    [Fact]
    public void Unknown_Side_Exposure_Does_Not_Become_Zero()
    {
        var unknownLongValue = CreatePosition(PositionSide.Long, null, 1m);
        var knownShort = CreatePosition(PositionSide.Short, 10m, 1m);
        var state = CreateState([unknownLongValue, knownShort]);

        state.LongExposure.Should().BeNull();
        state.ShortExposure.Should().Be(10m);
        state.NetExposure.Should().BeNull();
    }

    [Fact]
    public void Closed_Position_From_Other_Account_Is_Rejected()
    {
        var key = ExchangePositionKey.Create(
            ExchangeAccountId.New(), InstrumentId.From("ETHUSDT"), PositionSide.Long, 0);
        var position = Position.Create(key, MarketCategory.Linear, 1m, T0, T0);
        position.Close(T0.AddMinutes(1));

        FluentActions.Invoking(() => CreateState([position]))
            .Should().Throw<ArgumentException>();
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
        decimal? equity = 100m,
        decimal? available = 60m,
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
