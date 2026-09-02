using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Application.Market;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Public;

internal sealed class BybitPublicMarketProvider : IMarketDataProvider, IDerivativesDataProvider
{
    private readonly IBybitRestClient _client;
    private readonly ILogger<BybitPublicMarketProvider> _logger;

    public BybitPublicMarketProvider(IBybitRestClient client, ILogger<BybitPublicMarketProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Kline>> GetKlinesAsync(string symbol, MarketCategory category, KlineInterval interval, DateTime? startTime = null, DateTime? endTime = null, int? limit = null, CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetKlinesAsync(category.ToBybitCategory(), symbol, interval.ToBybitInterval(), startTime, endTime, limit, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchKlines(_logger, symbol, category, interval, response.Error?.Message);
            return [];
        }

        return response.Data?.List?.Select(kline => kline.MapKline(symbol, category, interval)).ToList() ?? [];
    }

    public async Task<Ticker?> GetTickerAsync(string symbol, MarketCategory category, CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            var response = await _client.V5Api.ExchangeData.GetSpotTickersAsync(symbol, cancellationToken);
            if (!response.Success)
            {
                BybitPublicProviderLogMessages.LogFailedToFetchSpotTicker(_logger, symbol, response.Error?.Message);
                return null;
            }

            return response.Data?.List?.FirstOrDefault()?.MapSpotTicker(symbol);
        }

        var linearInverseResponse = await _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(category.ToBybitCategory(), symbol, null, null, cancellationToken);
        if (!linearInverseResponse.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchTicker(_logger, symbol, category, linearInverseResponse.Error?.Message);
            return null;
        }

        return linearInverseResponse.Data?.List?.FirstOrDefault()?.MapLinearInverseTicker(symbol, category);
    }

    public async Task<OrderBook?> GetOrderBookAsync(string symbol, MarketCategory category, int limit = 50, CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetOrderbookAsync(category.ToBybitCategory(), symbol, limit, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchOrderBook(_logger, symbol, category, response.Error?.Message);
            return null;
        }

        return response.Data?.MapOrderBook(category);
    }

    public async Task<IReadOnlyList<Trade>> GetRecentTradesAsync(string symbol, MarketCategory category, int limit = 60, CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetTradeHistoryAsync(category.ToBybitCategory(), symbol, null, null, limit, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchRecentTrades(_logger, symbol, category, response.Error?.Message);
            return [];
        }

        return response.Data?.List?.Select(trade => trade.MapTrade(symbol, category)).ToList() ?? [];
    }

    public async Task<IReadOnlyList<OpenInterestEntry>> GetOpenInterestHistoryAsync(string symbol, MarketCategory category, OpenInterestInterval interval, DateTime? startTime = null, DateTime? endTime = null, int? limit = 48, CancellationToken cancellationToken = default)
    {
        EnsureDerivativesCategory(category, "Open interest data");
        var response = await _client.V5Api.ExchangeData.GetOpenInterestAsync(category.ToBybitCategory(), symbol, interval.ToBybitOpenInterestInterval(), startTime, endTime, limit, null, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchOpenInterest(_logger, symbol, category, interval, response.Error?.Message);
            return [];
        }

        return response.Data?.List?.Select(entry => entry.MapOpenInterestEntry(symbol, category)).ToList() ?? [];
    }

    public async Task<IReadOnlyList<FundingRateEntry>> GetFundingRateHistoryAsync(string symbol, MarketCategory category, DateTime? startTime = null, DateTime? endTime = null, int? limit = 30, CancellationToken cancellationToken = default)
    {
        EnsureDerivativesCategory(category, "Funding rate data");
        var response = await _client.V5Api.ExchangeData.GetFundingRateHistoryAsync(category.ToBybitCategory(), symbol, startTime, endTime, limit, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchFundingRateHistory(_logger, symbol, category, response.Error?.Message);
            return [];
        }

        return response.Data?.List?.Select(entry => entry.MapFundingRateEntry(symbol, category)).ToList() ?? [];
    }

    public async Task<IReadOnlyList<LongShortRatioEntry>> GetLongShortRatioHistoryAsync(string symbol, MarketCategory category, LongShortRatioPeriod period, DateTime? startTime = null, DateTime? endTime = null, int? limit = 50, CancellationToken cancellationToken = default)
    {
        EnsureDerivativesCategory(category, "Long/short ratio data");
        var response = await _client.V5Api.ExchangeData.GetLongShortRatioAsync(category.ToBybitCategory(), symbol, period.ToBybitDataPeriod(), startTime, endTime, limit, cancellationToken);
        if (!response.Success)
        {
            BybitPublicProviderLogMessages.LogFailedToFetchLongShortRatio(_logger, symbol, category, period, response.Error?.Message);
            return [];
        }

        return response.Data?.Select(entry => entry.MapLongShortRatioEntry(symbol, category)).ToList() ?? [];
    }

    private static void EnsureDerivativesCategory(MarketCategory category, string operation)
    {
        if (category == MarketCategory.Spot)
            throw new ArgumentException($"{operation} is not available for the Spot market. Use Linear or Inverse.", nameof(category));
    }
}
