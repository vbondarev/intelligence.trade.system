using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.Market.Positions;

public sealed record PositionMarketContext(
    PositionMarketIdentity Identity,
    MarketSnapshot Snapshot);

public sealed record PositionCandles(
    PositionMarketIdentity Identity,
    KlineInterval Interval,
    IReadOnlyList<Kline> Items);
