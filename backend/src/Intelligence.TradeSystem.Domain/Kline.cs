namespace Intelligence.TradeSystem.Domain;

public sealed record Kline(
    string Symbol,
    MarketCategory Category,
    KlineInterval Interval,
    DateTime StartTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover
);

