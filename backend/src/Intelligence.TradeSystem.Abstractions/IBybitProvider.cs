using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Abstractions;

public interface IBybitProvider
{
    Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default);
}

