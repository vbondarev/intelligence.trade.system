using Intelligence.TradeSystem.Application.Portfolio.Read;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;
using Intelligence.TradeSystem.Domain.Snapshots;
using Moq;

namespace Intelligence.TradeSystem.Application.Tests.Portfolio;

public sealed class PositionReadServiceTests
{
    [Fact]
    public void Query_defaults_to_the_active_working_states_and_normalizes_symbol()
    {
        var query = PositionReadQuery.Create(
            ExchangeAccountId.New(),
            trackingState: null,
            symbol: "  btcusdt  ",
            side: PositionSide.Long,
            pageSize: 50,
            cursor: null);

        query.TrackingStates.Should().Equal(
            PositionTrackingState.Active,
            PositionTrackingState.Unknown,
            PositionTrackingState.Stale);
        query.Symbol.Should().Be("btcusdt");
        query.Side.Should().Be(PositionSide.Long);
    }

    [Fact]
    public async Task Service_forwards_only_typed_scope_and_query_to_the_store()
    {
        var userId = UserId.New();
        var query = PositionReadQuery.Create(
            null,
            PositionTrackingState.Closed,
            "BTCUSDT",
            PositionSide.Short,
            10,
            new PositionReadCursor(DateTimeOffset.UtcNow, PositionId.New()));
        var expected = new PositionReadPage([], null, false);
        var store = new Mock<IPositionReadStore>(MockBehavior.Strict);
        store.Setup(x => x.ListAsync(userId, query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var service = new PositionReadService(store.Object);

        var result = await service.ListAsync(userId, query);

        result.Should().BeSameAs(expected);
        store.VerifyAll();
    }
}
