using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Analysis.Assemblers;

/// <summary>
/// Собирает <see cref="OrderBookSnapshot"/> из сырых данных стакана <see cref="OrderBook"/>.
/// <para>
/// Порядок преобразований:
/// <list type="number">
///   <item>Валидация входного стакана</item>
///   <item>Вычисление mid-price из лучших бида и аска</item>
///   <item>Агрегация объёмов и дисбалансов на глубинах 5 / 10 / 20</item>
///   <item>Обнаружение ликвидных стен по порогу <see cref="WallThresholdMultiplier"/></item>
///   <item>Сборка снимка</item>
/// </list>
/// </para>
/// </summary>
public static class OrderBookSnapshotAssembler
{
    /// <summary>
    /// Множитель среднего объёма, при превышении которого уровень считается ликвидной стеной.
    /// Уровень с объёмом выше <c>avgSize × WallThresholdMultiplier</c> фиксируется как стена.
    /// </summary>
    internal const decimal WallThresholdMultiplier = 3m;

    /// <summary>
    /// Вычисляет и возвращает <see cref="OrderBookSnapshot"/> для переданного стакана.
    /// </summary>
    /// <param name="orderBook">Сырые данные стакана с биржи.</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="orderBook"/> равен <c>null</c>.</exception>
    /// <exception cref="ArgumentException">Если стакан не содержит ни одного уровня бидов или асков.</exception>
    public static OrderBookSnapshot Assemble(OrderBook orderBook)
    {
        ArgumentNullException.ThrowIfNull(orderBook);

        if (orderBook.Bids.Count == 0 || orderBook.Asks.Count == 0)
        {
            throw new ArgumentException("Order book must contain at least one bid and one ask level.", nameof(orderBook));
        }

        // 1. Mid price
        var midPrice = (orderBook.Bids[0].Price + orderBook.Asks[0].Price) / 2m;

        // 2. Aggregate volumes top 5 / 10 / 20
        var bidTop5 = VolumeSum(orderBook.Bids, 5);
        var bidTop10 = VolumeSum(orderBook.Bids, 10);
        var bidTop20 = VolumeSum(orderBook.Bids, 20);

        var askTop5 = VolumeSum(orderBook.Asks, 5);
        var askTop10 = VolumeSum(orderBook.Asks, 10);
        var askTop20 = VolumeSum(orderBook.Asks, 20);

        var imbalanceTop5 = Imbalance(bidTop5, askTop5);
        var imbalanceTop10 = Imbalance(bidTop10, askTop10);
        var imbalanceTop20 = Imbalance(bidTop20, askTop20);

        // 3. Top levels (up to 20)
        var topBids = orderBook.Bids.Take(20)
            .Select(e => new OrderBookLevel { Price = e.Price, Size = e.Size })
            .ToList();

        var topAsks = orderBook.Asks.Take(20)
            .Select(e => new OrderBookLevel { Price = e.Price, Size = e.Size })
            .ToList();

        // 4. Walls
        var bidWalls = DetectWalls(orderBook.Bids, midPrice);
        var askWalls = DetectWalls(orderBook.Asks, midPrice);

        // 5. Assemble
        return new OrderBookSnapshot
        {
            CapturedAtUtc = orderBook.CapturedAt,

            BestBidPrice = orderBook.Bids[0].Price,
            BestAskPrice = orderBook.Asks[0].Price,

            TotalBidVolumeTop5 = bidTop5,
            TotalAskVolumeTop5 = askTop5,
            TotalBidVolumeTop10 = bidTop10,
            TotalAskVolumeTop10 = askTop10,
            TotalBidVolumeTop20 = bidTop20,
            TotalAskVolumeTop20 = askTop20,

            ImbalanceTop5 = imbalanceTop5,
            ImbalanceTop10 = imbalanceTop10,
            ImbalanceTop20 = imbalanceTop20,

            TopBids = topBids,
            TopAsks = topAsks,

            BidWalls = bidWalls,
            AskWalls = askWalls,
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static decimal VolumeSum(IReadOnlyList<OrderBookEntry> levels, int depth) =>
        levels.Take(depth).Sum(e => e.Size);

    private static decimal Imbalance(decimal bid, decimal ask)
    {
        var total = bid + ask;
        return total > 0m ? Math.Round((bid - ask) / total, 4) : 0m;
    }

    private static List<LiquidityWall> DetectWalls(IReadOnlyList<OrderBookEntry> levels, decimal midPrice)
    {
        var relevant = levels.Take(20).ToList();
        if (relevant.Count == 0) return [];

        var avgSize = relevant.Average(e => e.Size);
        var threshold = avgSize * WallThresholdMultiplier;

        return relevant
            .Where(e => e.Size > threshold)
            .Select(e => new LiquidityWall
            {
                Price = e.Price,
                Size = e.Size,
                DistancePctFromMarket = midPrice > 0m
                    ? Math.Round(Math.Abs(e.Price - midPrice) / midPrice * 100m, 4)
                    : 0m,
            })
            .ToList();
    }
}
