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
}
