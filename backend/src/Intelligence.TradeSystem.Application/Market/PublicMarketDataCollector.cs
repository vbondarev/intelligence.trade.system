using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Application.Market;

/// <summary>
/// Тонкий orchestration-компонент, параллельно собирающий сырые публичные рыночные данные
/// через capability-интерфейсы биржевого слоя.
/// </summary>
public sealed class PublicMarketDataCollector : IPublicMarketDataCollector
{
    private const int KlineLimit = 250;
    private const int RecentTradesLimit = 60;
    private const int OrderBookDepth = 50;
    private const int OpenInterestLimit = 48;
    private const int FundingRateLimit = 30;
    private const int LongShortRatioLimit = 50;
    private const OpenInterestInterval DefaultOpenInterestInterval = OpenInterestInterval.FiveMinutes;
    private const LongShortRatioPeriod DefaultLongShortRatioPeriod = LongShortRatioPeriod.FiveMinutes;

    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IDerivativesDataProvider _derivativesDataProvider;

    public PublicMarketDataCollector(
        IMarketDataProvider marketDataProvider,
        IDerivativesDataProvider derivativesDataProvider)
    {
        _marketDataProvider = marketDataProvider;
        _derivativesDataProvider = derivativesDataProvider;
    }

    public async Task<CollectedPublicMarketData> CollectAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureExchangeIsSupported(exchangeId);

        var normalizedSymbol = NormalizeSymbol(symbol);

        var tickerTask = _marketDataProvider.GetTickerAsync(normalizedSymbol, category, cancellationToken);
        var orderBookTask = _marketDataProvider.GetOrderBookAsync(normalizedSymbol, category, OrderBookDepth, cancellationToken);
        var tradesTask = _marketDataProvider.GetRecentTradesAsync(normalizedSymbol, category, RecentTradesLimit, cancellationToken);

        var m15KlinesTask = _marketDataProvider.GetKlinesAsync(
            normalizedSymbol,
            category,
            KlineInterval.FifteenMinutes,
            limit: KlineLimit,
            cancellationToken: cancellationToken);

        var h1KlinesTask = _marketDataProvider.GetKlinesAsync(
            normalizedSymbol,
            category,
            KlineInterval.OneHour,
            limit: KlineLimit,
            cancellationToken: cancellationToken);

        var h4KlinesTask = _marketDataProvider.GetKlinesAsync(
            normalizedSymbol,
            category,
            KlineInterval.FourHours,
            limit: KlineLimit,
            cancellationToken: cancellationToken);

        var d1KlinesTask = _marketDataProvider.GetKlinesAsync(
            normalizedSymbol,
            category,
            KlineInterval.OneDay,
            limit: KlineLimit,
            cancellationToken: cancellationToken);

        var requiresDerivativesData = category is MarketCategory.Linear or MarketCategory.Inverse;

        var openInterestTask = requiresDerivativesData
            ? _derivativesDataProvider.GetOpenInterestHistoryAsync(
                normalizedSymbol,
                category,
                DefaultOpenInterestInterval,
                limit: OpenInterestLimit,
                cancellationToken: cancellationToken)
            : Task.FromResult<IReadOnlyList<OpenInterestEntry>>([]);

        var fundingRateTask = requiresDerivativesData
            ? _derivativesDataProvider.GetFundingRateHistoryAsync(
                normalizedSymbol,
                category,
                limit: FundingRateLimit,
                cancellationToken: cancellationToken)
            : Task.FromResult<IReadOnlyList<FundingRateEntry>>([]);

        var longShortRatioTask = requiresDerivativesData
            ? _derivativesDataProvider.GetLongShortRatioHistoryAsync(
                normalizedSymbol,
                category,
                DefaultLongShortRatioPeriod,
                limit: LongShortRatioLimit,
                cancellationToken: cancellationToken)
            : Task.FromResult<IReadOnlyList<LongShortRatioEntry>>([]);

        await Task.WhenAll(
            tickerTask,
            orderBookTask,
            tradesTask,
            m15KlinesTask,
            h1KlinesTask,
            h4KlinesTask,
            d1KlinesTask,
            openInterestTask,
            fundingRateTask,
            longShortRatioTask);

        return new CollectedPublicMarketData
        {
            ExchangeId = exchangeId,
            Symbol = normalizedSymbol,
            Category = category,
            Ticker = tickerTask.Result,
            OrderBook = orderBookTask.Result,
            Trades = tradesTask.Result,
            M15Klines = m15KlinesTask.Result,
            H1Klines = h1KlinesTask.Result,
            H4Klines = h4KlinesTask.Result,
            D1Klines = d1KlinesTask.Result,
            OpenInterestEntries = openInterestTask.Result,
            OpenInterestInterval = DefaultOpenInterestInterval,
            FundingRateEntries = fundingRateTask.Result,
            LongShortRatioEntries = longShortRatioTask.Result,
            LongShortRatioPeriod = DefaultLongShortRatioPeriod,
        };
    }

    private static void EnsureExchangeIsSupported(ExchangeId exchangeId)
    {
        if (exchangeId != ExchangeId.Bybit)
        {
            throw new NotSupportedException($"Exchange '{exchangeId}' is not supported by the current collector configuration.");
        }
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim();
    }
}
