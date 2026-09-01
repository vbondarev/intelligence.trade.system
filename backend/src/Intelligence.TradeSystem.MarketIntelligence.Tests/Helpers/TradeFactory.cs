using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;

/// <summary>
/// Фабрика тестовых сделок с детерминированными значениями по умолчанию.
/// Позволяет в тестах переопределять только значимые поля.
/// </summary>
public static class TradeFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static Trade Create(
        DateTimeOffset? timestamp = null,
        TradeSide side = TradeSide.Buy,
        decimal quantity = 1m,
        decimal price = 100m,
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear) =>
        new(
            symbol,
            category,
            timestamp ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            side,
            quantity,
            price);
}
