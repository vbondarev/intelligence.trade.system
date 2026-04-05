using FluentAssertions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Analysis.Tests.Helpers;
using Intelligence.TradeSystem.Domain;
using Xunit;

namespace Intelligence.TradeSystem.Analysis.Tests.Assemblers;

public sealed class OrderBookSnapshotAssemblerTests
{
    [Fact]
    public void Throws_ArgumentNullException_When_OrderBook_Is_Null()
    {
        var act = () => OrderBookSnapshotAssembler.Assemble(null!);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("orderBook");
    }

    [Fact]
    public void Throws_ArgumentException_When_Bids_Are_Empty()
    {
        var orderBook = OrderBookFactory.Create(
            bids: [],
            asks: [OrderBookFactory.Level(101m, 10m)]);

        var act = () => OrderBookSnapshotAssembler.Assemble(orderBook);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("orderBook");
    }

    [Fact]
    public void Throws_ArgumentException_When_Asks_Are_Empty()
    {
        var orderBook = OrderBookFactory.Create(
            bids: [OrderBookFactory.Level(99m, 10m)],
            asks: []);

        var act = () => OrderBookSnapshotAssembler.Assemble(orderBook);

        act.Should()
            .Throw<ArgumentException>()
            .WithParameterName("orderBook");
    }

    [Fact]
    public void Maps_CapturedAt_And_Best_Prices_From_First_Levels()
    {
        var capturedAt = new DateTimeOffset(2024, 1, 2, 12, 30, 0, TimeSpan.Zero);
        var orderBook = OrderBookFactory.Create(
            bids:
            [
                OrderBookFactory.Level(100m, 10m),
                OrderBookFactory.Level(99m, 20m)
            ],
            asks:
            [
                OrderBookFactory.Level(101m, 15m),
                OrderBookFactory.Level(102m, 25m)
            ],
            capturedAt: capturedAt);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.CapturedAtUtc.Should().Be(capturedAt);
        result.BestBidPrice.Should().Be(100m);
        result.BestAskPrice.Should().Be(101m);
    }

    [Fact]
    public void Aggregates_Bid_And_Ask_Volumes_For_Top5_Top10_And_Top20()
    {
        var bids = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(100m - i, i + 1m))
            .ToList();

        var asks = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(101m + i, i + 2m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.TotalBidVolumeTop5.Should().Be(15m);   // 1+2+3+4+5
        result.TotalAskVolumeTop5.Should().Be(20m);   // 2+3+4+5+6
        result.TotalBidVolumeTop10.Should().Be(55m);  // 1..10
        result.TotalAskVolumeTop10.Should().Be(65m);  // 2..11
        result.TotalBidVolumeTop20.Should().Be(210m); // 1..20
        result.TotalAskVolumeTop20.Should().Be(230m); // 2..21
    }

    [Fact]
    public void Ignores_Levels_Beyond_Top20_When_Aggregating_Volumes()
    {
        var bids = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(100m - i, 1m))
            .Append(OrderBookFactory.Level(1m, 10_000m))
            .ToList();

        var asks = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(101m + i, 2m))
            .Append(OrderBookFactory.Level(500m, 20_000m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.TotalBidVolumeTop20.Should().Be(20m);
        result.TotalAskVolumeTop20.Should().Be(40m);
    }

    [Fact]
    public void Computes_Imbalance_For_Top5_Top10_And_Top20()
    {
        var bids = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(100m - i, i + 1m))
            .ToList();

        var asks = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(101m + i, i + 2m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.ImbalanceTop5.Should().Be(-0.1429m);  // (15-20)/(15+20)
        result.ImbalanceTop10.Should().Be(-0.0833m); // (55-65)/(55+65)
        result.ImbalanceTop20.Should().Be(-0.0455m); // (210-230)/(210+230)
    }

    [Fact]
    public void Returns_Zero_Imbalance_When_Total_Volume_Is_Zero()
    {
        var bids = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(100m - i, 0m))
            .ToList();

        var asks = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(101m + i, 0m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.ImbalanceTop5.Should().Be(0m);
        result.ImbalanceTop10.Should().Be(0m);
        result.ImbalanceTop20.Should().Be(0m);
    }

    [Fact]
    public void Copies_Only_First_20_Bid_And_Ask_Levels_To_TopLevels()
    {
        var bids = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(100m - i, i + 1m))
            .ToList();

        var asks = Enumerable.Range(0, 25)
            .Select(i => OrderBookFactory.Level(101m + i, i + 2m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.TopBids.Should().HaveCount(20);
        result.TopAsks.Should().HaveCount(20);

        result.TopBids[0].Price.Should().Be(100m);
        result.TopBids[0].Size.Should().Be(1m);
        result.TopBids[^1].Price.Should().Be(81m);
        result.TopBids[^1].Size.Should().Be(20m);

        result.TopAsks[0].Price.Should().Be(101m);
        result.TopAsks[0].Size.Should().Be(2m);
        result.TopAsks[^1].Price.Should().Be(120m);
        result.TopAsks[^1].Size.Should().Be(21m);
    }

    [Fact]
    public void Detects_Bid_And_Ask_Walls_When_Level_Size_Is_Greater_Than_Three_Times_Average()
    {
        var bids = new List<OrderBookEntry>
        {
            OrderBookFactory.Level(99m, 10m),
            OrderBookFactory.Level(98m, 10m),
            OrderBookFactory.Level(97m, 10m),
            OrderBookFactory.Level(96m, 10m),
            OrderBookFactory.Level(95m, 100m),
        };
        bids.AddRange(Enumerable.Range(0, 15).Select(i => OrderBookFactory.Level(94m - i, 10m)));

        var asks = new List<OrderBookEntry>
        {
            OrderBookFactory.Level(101m, 10m),
            OrderBookFactory.Level(102m, 10m),
            OrderBookFactory.Level(103m, 10m),
            OrderBookFactory.Level(104m, 10m),
            OrderBookFactory.Level(105m, 100m),
        };
        asks.AddRange(Enumerable.Range(0, 15).Select(i => OrderBookFactory.Level(106m + i, 10m)));

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.BidWalls.Should().ContainSingle();
        result.AskWalls.Should().ContainSingle();

        result.BidWalls[0].Price.Should().Be(95m);
        result.BidWalls[0].Size.Should().Be(100m);
        result.BidWalls[0].DistancePctFromMarket.Should().Be(5m);

        result.AskWalls[0].Price.Should().Be(105m);
        result.AskWalls[0].Size.Should().Be(100m);
        result.AskWalls[0].DistancePctFromMarket.Should().Be(5m);
    }

    [Fact]
    public void Does_Not_Detect_Wall_When_Level_Size_Equals_Threshold()
    {
        // 19 уровней по 17 и один уровень 57:
        // avg = (19*17 + 57) / 20 = 19; threshold = 19 * 3 = 57.
        // Так как правило строгое (> threshold), размер 57 НЕ должен считаться стеной.
        var bids = Enumerable.Repeat(OrderBookFactory.Level(99m, 17m), 19)
            .Append(OrderBookFactory.Level(80m, 57m))
            .ToList();

        var asks = Enumerable.Repeat(OrderBookFactory.Level(101m, 17m), 19)
            .Append(OrderBookFactory.Level(120m, 57m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.BidWalls.Should().BeEmpty();
        result.AskWalls.Should().BeEmpty();
    }

    [Fact]
    public void Detects_Walls_Only_Within_Top20_Levels()
    {
        var bids = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(100m - i, 10m))
            .Append(OrderBookFactory.Level(1m, 1_000m))
            .ToList();

        var asks = Enumerable.Range(0, 20)
            .Select(i => OrderBookFactory.Level(101m + i, 10m))
            .Append(OrderBookFactory.Level(500m, 1_000m))
            .ToList();

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.BidWalls.Should().BeEmpty();
        result.AskWalls.Should().BeEmpty();
    }

    [Fact]
    public void Returns_Zero_Wall_DistancePct_When_MidPrice_Is_Zero()
    {
        var bids = new List<OrderBookEntry>
        {
            OrderBookFactory.Level(-1m, 10m),
            OrderBookFactory.Level(-2m, 100m)
        };
        bids.AddRange(Enumerable.Range(0, 18).Select(i => OrderBookFactory.Level(-3m - i, 10m)));

        var asks = new List<OrderBookEntry>
        {
            OrderBookFactory.Level(1m, 10m),
            OrderBookFactory.Level(2m, 100m)
        };
        asks.AddRange(Enumerable.Range(0, 18).Select(i => OrderBookFactory.Level(3m + i, 10m)));

        var orderBook = OrderBookFactory.Create(bids: bids, asks: asks);

        var result = OrderBookSnapshotAssembler.Assemble(orderBook);

        result.BidWalls.Should().ContainSingle();
        result.AskWalls.Should().ContainSingle();
        result.BidWalls[0].DistancePctFromMarket.Should().Be(0m);
        result.AskWalls[0].DistancePctFromMarket.Should().Be(0m);
    }
}

