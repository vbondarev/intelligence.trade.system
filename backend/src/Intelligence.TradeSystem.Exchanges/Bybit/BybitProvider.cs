using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.Logging;

namespace Intelligence.TradeSystem.Exchanges.Bybit;

internal sealed class BybitProvider : IBybitProvider
{
    private readonly IBybitRestClient _client;
    private readonly ILogger<BybitProvider> _logger;

    public BybitProvider(IBybitRestClient client, ILogger<BybitProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetKlinesAsync(
            category.ToBybitCategory(),
            symbol,
            interval.ToBybitInterval(),
            startTime,
            endTime,
            limit,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch klines for {Symbol} ({Category}, {Interval}): {Error}",
                symbol, category, interval, response.Error?.Message);
            return [];
        }

        return response.Data?.List?
            .Select(k => MapKline(symbol, category, interval, k))
            .ToList() ?? [];
    }

    public async Task<Ticker?> GetTickerAsync(
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            var response = await _client.V5Api.ExchangeData.GetSpotTickersAsync(
                symbol,
                cancellationToken);

            if (!response.Success)
            {
                _logger.LogError(
                    "Failed to fetch spot ticker for {Symbol}: {Error}",
                    symbol, response.Error?.Message);
                return null;
            }

            var ticker = response.Data?.List?.FirstOrDefault();
            return ticker is null ? null : MapSpotTicker(symbol, ticker);
        }
        else
        {
            var response = await _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(
                category.ToBybitCategory(),
                symbol,
                null,
                null,
                cancellationToken);

            if (!response.Success)
            {
                _logger.LogError(
                    "Failed to fetch ticker for {Symbol} ({Category}): {Error}",
                    symbol, category, response.Error?.Message);
                return null;
            }

            var ticker = response.Data?.List?.FirstOrDefault();
            return ticker is null ? null : MapLinearInverseTicker(symbol, category, ticker);
        }
    }

    public async Task<OrderBook?> GetOrderBookAsync(
        string symbol,
        MarketCategory category,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetOrderbookAsync(
            category.ToBybitCategory(),
            symbol,
            limit,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch order book for {Symbol} ({Category}): {Error}",
                symbol, category, response.Error?.Message);
            return null;
        }

        return response.Data is null ? null : MapOrderBook(response.Data, category);
    }

    private static Kline MapKline(
        string symbol,
        MarketCategory category,
        KlineInterval interval,
        BybitKline kline) =>
        new(symbol, category, interval,
            kline.StartTime,
            kline.OpenPrice,
            kline.HighPrice,
            kline.LowPrice,
            kline.ClosePrice,
            kline.Volume,
            kline.QuoteVolume);

    private static Ticker MapLinearInverseTicker(
        string symbol,
        MarketCategory category,
        BybitLinearInverseTicker t) =>
        new(symbol,
            category,
            t.LastPrice,
            t.MarkPrice,
            t.IndexPrice,
            t.BestBidPrice    ?? 0m,
            t.BestBidQuantity ?? 0m,
            t.BestAskPrice    ?? 0m,
            t.BestAskQuantity ?? 0m,
            t.PriceChangePercentage24h,
            t.HighPrice24h,
            t.LowPrice24h,
            t.Volume24h,
            t.Turnover24h);

    private static Ticker MapSpotTicker(
        string symbol,
        BybitSpotTicker t) =>
        new(symbol,
            MarketCategory.Spot,
            t.LastPrice,
            MarkPrice:   0m,
            IndexPrice:  0m,
            t.BestBidPrice    ?? 0m,
            t.BestBidQuantity ?? 0m,
            t.BestAskPrice    ?? 0m,
            t.BestAskQuantity ?? 0m,
            t.PriceChangePercentag24h,
            t.HighPrice24h,
            t.LowPrice24h,
            t.Volume24h,
            t.Turnover24h);

    private static OrderBook MapOrderBook(BybitOrderbook ob, MarketCategory category) =>
        new(ob.Symbol,
            category,
            ob.Timestamp,
            ob.Bids.Select(e => new OrderBookEntry(e.Price, e.Quantity)).ToList(),
            ob.Asks.Select(e => new OrderBookEntry(e.Price, e.Quantity)).ToList());
}
