using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Analysis.Tests.Helpers;

/// <summary>
/// Фабрика тестовых точек long/short ratio с детерминированными значениями по умолчанию.
/// Позволяет в тестах переопределять только timestamp и ratios.
/// </summary>
public static class LongShortRatioEntryFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static LongShortRatioEntry Create(
        DateTimeOffset? timestamp = null,
        decimal buyRatio = 0.5m,
        decimal sellRatio = 0.5m,
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear) =>
        new(
            symbol,
            category,
            timestamp ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            buyRatio,
            sellRatio);
}

