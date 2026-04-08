using Intelligence.TradeSystem.Abstractions;
using Intelligence.TradeSystem.Analysis.Assemblers;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;

namespace Intelligence.TradeSystem.Application;

/// <summary>
/// Оркестрирует сбор сырых данных и последовательную сборку всех аналитических снапшотов.
/// </summary>
public sealed class MarketAnalysisService : IMarketAnalysisService
{
    private readonly IMarketDataCollector _marketDataCollector;

    public MarketAnalysisService(IMarketDataCollector marketDataCollector)
    {
        _marketDataCollector = marketDataCollector;
    }

    /// <inheritdoc />
    public async Task<MarketAnalysisSnapshot> BuildSnapshotAsync(
        ExchangeId exchangeId,
        string symbol,
        MarketCategory category,
        CancellationToken cancellationToken = default)
    {
        EnsureExchangeIsSupported(exchangeId);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var collectedData = await _marketDataCollector.CollectAsync(exchangeId, normalizedSymbol, category, cancellationToken);

        ArgumentNullException.ThrowIfNull(collectedData);

        ValidateRequiredData(collectedData);

        var price = PriceSnapshotAssembler.Assemble(collectedData.Ticker!);

        var fundingRateSnapshot = collectedData.FundingRateEntries.Count > 0
            ? FundingRateSnapshotAssembler.Assemble(collectedData.FundingRateEntries)
            : null;

        var openInterestSnapshot = collectedData.OpenInterestEntries.Count > 0
            ? OpenInterestSnapshotAssembler.Assemble(collectedData.OpenInterestEntries, collectedData.OpenInterestInterval)
            : null;

        var longShortRatioSnapshot = collectedData.LongShortRatioEntries.Count > 0
            ? LongShortRatioSnapshotAssembler.Assemble(collectedData.LongShortRatioEntries, collectedData.LongShortRatioPeriod)
            : null;

        var derivatives = DerivativesSnapshotAssembler.Assemble(
            collectedData.Ticker!,
            fundingRateSnapshot,
            openInterestSnapshot,
            longShortRatioSnapshot);

        var orderBook = OrderBookSnapshotAssembler.Assemble(collectedData.OrderBook!);
        var tradeFlow = TradeFlowSnapshotAssembler.Assemble(collectedData.Trades);

        var m15 = TimeframeSnapshotAssembler.Assemble(collectedData.M15Klines, "15m");
        var h1 = TimeframeSnapshotAssembler.Assemble(collectedData.H1Klines, "1h");
        var h4 = TimeframeSnapshotAssembler.Assemble(collectedData.H4Klines, "4h");
        var d1 = TimeframeSnapshotAssembler.Assemble(collectedData.D1Klines, "1d");

        var sentiment = SentimentSnapshotAssembler.Assemble(derivatives, orderBook, tradeFlow, h1, h4);
        var portfolio = PortfolioSnapshotAssembler.Assemble(collectedData.WalletBalance, collectedData.OpenPositions);

        return MarketAnalysisSnapshotAssembler.Assemble(
            exchangeId.ToString(),
            normalizedSymbol,
            category,
            price,
            derivatives,
            orderBook,
            tradeFlow,
            m15,
            h1,
            h4,
            d1,
            sentiment,
            portfolio);
    }

    private static void EnsureExchangeIsSupported(ExchangeId exchangeId)
    {
        if (exchangeId != ExchangeId.Bybit)
        {
            throw new NotSupportedException($"Exchange '{exchangeId}' is not supported by the current analysis service configuration.");
        }
    }

    private static string NormalizeSymbol(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim();
    }

    private static void ValidateRequiredData(CollectedMarketData collectedData)
    {
        if (collectedData.Ticker is null)
        {
            throw new InvalidOperationException($"Failed to collect ticker for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.OrderBook is null)
        {
            throw new InvalidOperationException($"Failed to collect order book for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.Trades.Count == 0)
        {
            throw new InvalidOperationException($"Failed to collect recent trades for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.M15Klines.Count == 0)
        {
            throw new InvalidOperationException($"Failed to collect 15m klines for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.H1Klines.Count == 0)
        {
            throw new InvalidOperationException($"Failed to collect 1h klines for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.H4Klines.Count == 0)
        {
            throw new InvalidOperationException($"Failed to collect 4h klines for symbol '{collectedData.Symbol}'.");
        }

        if (collectedData.D1Klines.Count == 0)
        {
            throw new InvalidOperationException($"Failed to collect 1d klines for symbol '{collectedData.Symbol}'.");
        }

        ArgumentNullException.ThrowIfNull(collectedData.OpenPositions);
    }
}

