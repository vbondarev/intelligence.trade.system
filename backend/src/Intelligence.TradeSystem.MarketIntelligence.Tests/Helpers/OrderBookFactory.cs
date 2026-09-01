using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;

/// <summary>
/// Фабрика тестовых стаканов с детерминированными значениями по умолчанию.
/// Позволяет в тестах переопределять только значимые уровни.
/// </summary>
public static class OrderBookFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static OrderBookEntry Level(decimal price, decimal size) => new(price, size);

    public static OrderBook Create(
        IReadOnlyList<OrderBookEntry>? bids = null,
        IReadOnlyList<OrderBookEntry>? asks = null,
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear,
        DateTimeOffset? capturedAt = null) =>
        new(
            symbol,
            category,
            capturedAt ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            bids ?? [Level(99m, 10m)],
            asks ?? [Level(101m, 12m)]);
}
