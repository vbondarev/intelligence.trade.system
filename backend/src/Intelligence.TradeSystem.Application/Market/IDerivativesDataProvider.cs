using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Нейтральный контракт доступа к публичным деривативным данным биржи.
/// </summary>
public interface IDerivativesDataProvider
{
    Task<IReadOnlyList<OpenInterestEntry>> GetOpenInterestHistoryAsync(
        string symbol,
        MarketCategory category,
        OpenInterestInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 48,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FundingRateEntry>> GetFundingRateHistoryAsync(
        string symbol,
        MarketCategory category,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 30,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LongShortRatioEntry>> GetLongShortRatioHistoryAsync(
        string symbol,
        MarketCategory category,
        LongShortRatioPeriod period,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 50,
        CancellationToken cancellationToken = default);
}
