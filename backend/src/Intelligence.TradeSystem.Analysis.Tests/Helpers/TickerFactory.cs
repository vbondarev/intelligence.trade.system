using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Analysis.Tests.Helpers;

/// <summary>
/// Фабрика тестовых тикеров с детерминированными значениями по умолчанию.
/// Позволяет в каждом тесте переопределять только значимые поля.
/// </summary>
public static class TickerFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static Ticker Create(
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear,
        decimal lastPrice = 100m,
        decimal markPrice = 100m,
        decimal indexPrice = 100m,
        decimal bidPrice = 99m,
        decimal bidSize = 10m,
        decimal askPrice = 101m,
        decimal askSize = 12m,
        decimal price24hChangePct = 0.02m,
        decimal high24h = 110m,
        decimal low24h = 90m,
        decimal volume24h = 1_000m,
        decimal turnover24h = 100_000m) =>
        new(
            symbol,
            category,
            lastPrice,
            markPrice,
            indexPrice,
            bidPrice,
            bidSize,
            askPrice,
            askSize,
            price24hChangePct,
            high24h,
            low24h,
            volume24h,
            turnover24h);
}
