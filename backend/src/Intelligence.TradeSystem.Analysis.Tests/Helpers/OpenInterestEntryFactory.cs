using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Analysis.Tests.Helpers;

/// <summary>
/// Фабрика тестовых точек открытого интереса с детерминированными значениями по умолчанию.
/// Позволяет в тестах переопределять только timestamp и open interest.
/// </summary>
public static class OpenInterestEntryFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static OpenInterestEntry Create(
        DateTimeOffset? timestamp = null,
        decimal openInterest = 100m,
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear) =>
        new(
            symbol,
            category,
            timestamp ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            openInterest);
}

