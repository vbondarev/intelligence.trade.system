using Bybit.Net.Interfaces.Clients;
using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Domain;
using Microsoft.Extensions.Logging;
using KlineInterval = Intelligence.TradeSystem.Domain.KlineInterval;

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
            .Select(kline => kline.MapKline(symbol, category, interval))
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
            return ticker?.MapSpotTicker(symbol);
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
            return ticker?.MapLinearInverseTicker(symbol, category);
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

        return response.Data?.MapOrderBook(category);
    }

    public async Task<IReadOnlyList<Trade>> GetRecentTradesAsync(
        string symbol,
        MarketCategory category,
        int limit = 60,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.ExchangeData.GetTradeHistoryAsync(
            category.ToBybitCategory(),
            symbol,
            null,
            null,
            limit,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch recent trades for {Symbol} ({Category}): {Error}",
                symbol, category, response.Error?.Message);
            return [];
        }

        return response.Data?.List?
            .Select(t => t.MapTrade(symbol, category))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<OpenInterestEntry>> GetOpenInterestHistoryAsync(
        string symbol,
        MarketCategory category,
        OpenInterestInterval interval,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 48,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException(
                "Open interest data is not available for the Spot market. Use Linear or Inverse.",
                nameof(category));
        }

        var response = await _client.V5Api.ExchangeData.GetOpenInterestAsync(
            category.ToBybitCategory(),
            symbol,
            interval.ToBybitOpenInterestInterval(),
            startTime,
            endTime,
            limit,
            null,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch open interest for {Symbol} ({Category}, {Interval}): {Error}",
                symbol, category, interval, response.Error?.Message);
            return [];
        }

        return response.Data?.List?
            .Select(e => e.MapOpenInterestEntry(symbol, category))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<FundingRateEntry>> GetFundingRateHistoryAsync(
        string symbol,
        MarketCategory category,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException(
                "Funding rate data is not available for the Spot market. Use Linear or Inverse.",
                nameof(category));
        }

        var response = await _client.V5Api.ExchangeData.GetFundingRateHistoryAsync(
            category.ToBybitCategory(),
            symbol,
            startTime,
            endTime,
            limit,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch funding rate history for {Symbol} ({Category}): {Error}",
                symbol, category, response.Error?.Message);
            return [];
        }

        return response.Data?.List?
            .Select(e => e.MapFundingRateEntry(symbol, category))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<LongShortRatioEntry>> GetLongShortRatioHistoryAsync(
        string symbol,
        MarketCategory category,
        LongShortRatioPeriod period,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException(
                "Long/short ratio data is not available for the Spot market. Use Linear or Inverse.",
                nameof(category));
        }

        var response = await _client.V5Api.ExchangeData.GetLongShortRatioAsync(
            category.ToBybitCategory(),
            symbol,
            period.ToBybitDataPeriod(),
            startTime,
            endTime,
            limit,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch long/short ratio for {Symbol} ({Category}, {Period}): {Error}",
                symbol, category, period, response.Error?.Message);
            return [];
        }

        return response.Data?
            .Select(e => e.MapLongShortRatioEntry(symbol, category))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<OpenPosition>> GetOpenPositionsAsync(
        MarketCategory category,
        string? symbol = null,
        CancellationToken cancellationToken = default)
    {
        if (category == MarketCategory.Spot)
        {
            throw new ArgumentException(
                "Position data is not available for the Spot market. Use Linear or Inverse.",
                nameof(category));
        }

        var response = await _client.V5Api.Trading.GetPositionsAsync(
            category.ToBybitCategory(),
            symbol,
            null,
            null,
            200,
            null,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch open positions ({Category}, {Symbol}): {Error}",
                category, symbol ?? "all", response.Error?.Message);
            return [];
        }

        return response.Data?.List?
            .Where(p => p.Quantity > 0m)
            .Select(p => p.MapOpenPosition(category))
            .ToList() ?? [];
    }

    public async Task<AccountBalance?> GetWalletBalanceAsync(
        AccountType accountType,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.V5Api.Account.GetBalancesAsync(
            accountType.ToBybitAccountType(),
            null,
            cancellationToken);

        if (!response.Success)
        {
            _logger.LogError(
                "Failed to fetch wallet balance ({AccountType}): {Error}",
                accountType, response.Error?.Message);
            return null;
        }

        var balance = response.Data?.List?.FirstOrDefault();
        return balance?.MapAccountBalance();
    }
}
