using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Helpers;

/// <summary>
/// Фабрика тестовых записей funding rate с детерминированными значениями по умолчанию.
/// Позволяет в тестах переопределять только timestamp и fundingRate.
/// </summary>
public static class FundingRateEntryFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    public static FundingRateEntry Create(
        DateTimeOffset? timestamp = null,
        decimal fundingRate = 0m,
        string symbol = DefaultSymbol,
        MarketCategory category = MarketCategory.Linear) =>
        new(
            symbol,
            category,
            timestamp ?? new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            fundingRate);
}
