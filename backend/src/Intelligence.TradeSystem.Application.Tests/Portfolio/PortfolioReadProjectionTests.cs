using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Portfolio;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PortfolioReadProjectionTests
{
    private static readonly ExchangeAccountId Account = ExchangeAccountId.New();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Usdt_Only_Produces_Single_Group()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, 100m, "USDT"),
            CreateState("SOLUSDT", PositionSide.Short, 40m, "USDT"),
        ]);

        exposures.Should().Equal(Exposure("USDT", 140m, 100m, 40m));
    }

    [Fact]
    public void Usdc_Only_Produces_Single_Group()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
            [CreateState("ETHUSDC", PositionSide.Long, 50m, "USDC")]);

        exposures.Should().Equal(Exposure("USDC", 50m, 50m, 0m));
    }

    [Fact]
    public void Usdt_And_Usdc_Are_Grouped_Separately_And_Ordered_By_Asset()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, 100m, "USDT"),
            CreateState("SOLUSDT", PositionSide.Short, 40m, "USDT"),
            CreateState("ETHUSDC", PositionSide.Long, 50m, "USDC"),
        ]);

        exposures.Should().Equal(
            Exposure("USDC", 50m, 50m, 0m),
            Exposure("USDT", 140m, 100m, 40m));
    }

    [Fact]
    public void Long_Only_Has_Zero_Short_Exposure()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, 100m, "USDT"),
            CreateState("ETHUSDT", PositionSide.Long, 30m, "USDT"),
        ]);

        exposures.Should().Equal(Exposure("USDT", 130m, 130m, 0m));
    }

    [Fact]
    public void Short_Only_Has_Zero_Long_Exposure()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
            [CreateState("BTCUSDT", PositionSide.Short, 70m, "USDT")]);

        exposures.Should().Equal(Exposure("USDT", 70m, 0m, 70m));
    }

    [Fact]
    public void Long_And_Short_Are_Summed_Per_Side_Inside_Group()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, 100m, "USDT"),
            CreateState("BTCUSDT", PositionSide.Short, 60m, "USDT"),
        ]);

        exposures.Should().Equal(Exposure("USDT", 160m, 100m, 60m));
    }

    [Fact]
    public void No_Positions_Produce_No_Groups()
    {
        PortfolioReadProjection.ProjectExposures([]).Should().BeEmpty();
    }

    [Fact]
    public void Unknown_Position_Value_Makes_Affected_Aggregates_Unknown()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, null, "USDT"),
            CreateState("SOLUSDT", PositionSide.Short, 40m, "USDT"),
        ]);

        exposures.Should().Equal(Exposure("USDT", null, null, 40m));
    }

    [Fact]
    public void Unknown_Value_In_One_Asset_Does_Not_Affect_Other_Asset()
    {
        var exposures = PortfolioReadProjection.ProjectExposures(
        [
            CreateState("BTCUSDT", PositionSide.Long, 100m, "USDT"),
            CreateState("ETHUSDC", PositionSide.Short, null, "USDC"),
            CreateState("SOLUSDC", PositionSide.Long, 20m, "USDC"),
        ]);

        exposures.Should().Equal(
            Exposure("USDC", null, 20m, null),
            Exposure("USDT", 100m, 100m, 0m));
    }

    [Fact]
    public void Project_Uses_Account_Pnl_Snapshot_Count_And_Read_Time_Freshness()
    {
        var usdt = CreatePosition("BTCUSDT", PositionSide.Long, 100m, 9m, "USDT");
        var usdc = CreatePosition("ETHUSDC", PositionSide.Short, 50m, 4m, "USDC");
        var state = PortfolioState.Create(
            Account,
            [usdt, usdc],
            new PortfolioCapitalState(1000m, 800m, T0, 900m, accountUnrealizedPnl: -77m),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        var fresh = PortfolioReadProjection.Project(state, T0.AddMinutes(2));
        var stale = PortfolioReadProjection.Project(state, T0.AddMinutes(30));

        fresh.TotalUnrealizedPnl.Should().Be(-77m);
        fresh.CurrentPositionCount.Should().Be(2);
        fresh.Exposures.Should().Equal(
            Exposure("USDC", 50m, 0m, 50m),
            Exposure("USDT", 100m, 100m, 0m));
        fresh.IsFresh.Should().BeTrue();
        stale.IsFresh.Should().BeFalse();
        state.IsFresh.Should().BeTrue();
    }

    [Fact]
    public void Project_Keeps_Unknown_Account_Pnl_Unknown()
    {
        var state = PortfolioState.Create(
            Account,
            [CreatePosition("BTCUSDT", PositionSide.Long, 100m, 9m, "USDT")],
            new PortfolioCapitalState(1000m, 800m, T0),
            T0.AddMinutes(1),
            TimeSpan.FromMinutes(5));

        PortfolioReadProjection.Project(state, T0.AddMinutes(1)).TotalUnrealizedPnl.Should().BeNull();
    }

    private static PortfolioExposureReadSummary Exposure(
        string asset,
        decimal? gross,
        decimal? longExposure,
        decimal? shortExposure) =>
        new(SettlementAsset.From(asset), gross, longExposure, shortExposure);

    private static Position CreatePosition(
        string symbol,
        PositionSide side,
        decimal? value,
        decimal? pnl,
        string settlementAsset) =>
        Position.Create(
            ExchangePositionKey.Create(Account, InstrumentId.From(symbol), side, 0),
            MarketCategory.Linear,
            SettlementAsset.From(settlementAsset),
            1m,
            T0,
            T0,
            positionValue: value,
            unrealizedPnl: pnl);

    private static PortfolioPositionState CreateState(
        string symbol,
        PositionSide side,
        decimal? value,
        string settlementAsset) =>
        new(
            PositionId.New(),
            ExchangePositionKey.Create(Account, InstrumentId.From(symbol), side, 0),
            MarketCategory.Linear,
            SettlementAsset.From(settlementAsset),
            side,
            PositionTrackingState.Active,
            1m,
            value,
            null,
            null,
            null,
            null,
            null,
            T0);
}
