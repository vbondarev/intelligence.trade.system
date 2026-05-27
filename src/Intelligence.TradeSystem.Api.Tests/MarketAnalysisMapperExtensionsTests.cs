using Intelligence.TradeSystem.Api.Contracts;
using Intelligence.TradeSystem.Api.Mappers;
using Intelligence.TradeSystem.Api.Tests.Helpers;

namespace Intelligence.TradeSystem.Api.Tests;

public sealed class MarketAnalysisMapperExtensionsTests
{
    [Fact]
    public void ToResponse_Throws_When_Snapshot_Is_Null()
    {
        var action = () => MarketAnalysisMapperExtensions.ToResponse(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("snapshot");
    }

    [Fact]
    public void ToResponse_Maps_All_Public_Sections_And_Stringifies_Enums()
    {
        var snapshot = ApiSnapshotTestData.CreateSnapshot();

        MarketAnalysisResponse response = snapshot.ToResponse();

        response.Exchange.Should().Be(snapshot.Exchange);
        response.Symbol.Should().Be(snapshot.Symbol);
        response.Category.Should().Be(snapshot.Category);
        response.CapturedAtUtc.Should().Be(snapshot.CapturedAtUtc);

        response.Price.LastPrice.Should().Be(snapshot.Price.LastPrice);
        response.Derivatives.PremiumVsIndexPct.Should().Be(snapshot.Derivatives.PremiumVsIndexPct);
        response.OrderBook.TopAsks.Should().ContainSingle(level => level.Price == 65005m && level.Size == 12m);
        response.OrderBook.BidWalls.Should().ContainSingle(wall => wall.Price == 64850m && wall.DistancePctFromMarket == 0.23m);
        response.TradeFlow.HasAggressiveSellPressure.Should().Be(snapshot.TradeFlow.HasAggressiveSellPressure);

        response.M15.Trend.Should().Be("Bullish");
        response.H1.IsAboveEma200.Should().BeTrue();
        response.H4.EmaBullishAlignment.Should().BeTrue();
        response.D1.LastCandle.Close.Should().Be(snapshot.D1.LastCandle.Close);

        response.Sentiment.MarketRegime.Should().Be(snapshot.Sentiment.MarketRegime);
        response.Portfolio.OpenPositions.Should().ContainSingle(position =>
             position.Symbol == "BTCUSDT" &&
             position.Side == "Long" &&
             position.UnrealizedPnlUsd == 500m);
        response.Tags.Should().Equal(snapshot.Tags);

        response.Tags.Should().NotBeSameAs(snapshot.Tags);
        ((object)response.OrderBook.TopBids).Should().NotBeSameAs(snapshot.OrderBook.TopBids);
        ((object)response.Portfolio.OpenPositions).Should().NotBeSameAs(snapshot.Portfolio.OpenPositions);
    }
}
