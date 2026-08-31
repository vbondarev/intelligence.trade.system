using System.Globalization;
using System.Text;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.MarketIntelligence.Snapshots;

namespace Intelligence.TradeSystem.Application.AI;

/// <summary>
/// Формирует компактный детерминированный текстовый контекст на основе <see cref="AiAnalysisContext"/>.
/// </summary>
public sealed class SnapshotTextFormatter : IAiContextFormatter
{
    private const string NotAvailable = "n/a";
    private const string None = "none";
    private static readonly CultureInfo _invariantCulture = CultureInfo.InvariantCulture;

    public string Format(AiAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder(capacity: 2048);

        AppendHeader(builder, context.Market);
        AppendPriceSection(builder, context.Market.Price);
        AppendDerivativesSection(builder, context.Market.Derivatives);
        AppendOrderBookSection(builder, context.Market.OrderBook);
        AppendTradeFlowSection(builder, context.Market.TradeFlow);
        AppendTrendSection(builder, context.Market);
        AppendSentimentSection(builder, context.Market.Sentiment);
        AppendPortfolioSection(builder, context.Portfolio);

        return builder.ToString().TrimEnd();
    }

    private static void AppendHeader(StringBuilder builder, MarketSnapshot snapshot)
    {
        builder.AppendLine("snapshot:");
        builder.Append("  exchange: ").AppendLine(snapshot.Exchange);
        builder.Append("  symbol: ").AppendLine(snapshot.Symbol);
        builder.Append("  category: ").AppendLine(snapshot.Category);
        builder.Append("  captured_at_utc: ").AppendLine(snapshot.CapturedAtUtc.ToString("O", _invariantCulture));
        builder.Append("  tags: ").AppendLine(FormatTags(snapshot.Tags));
        builder.AppendLine();
    }

    private static void AppendPriceSection(StringBuilder builder, PriceSnapshot price)
    {
        builder.AppendLine("price:");
        builder.Append("  last_price: ").AppendLine(FormatPriceLike(price.LastPrice));
        builder.Append("  mark_price: ").AppendLine(FormatPriceLike(price.MarkPrice));
        builder.Append("  index_price: ").AppendLine(FormatPriceLike(price.IndexPrice));
        builder.Append("  bid_price: ").AppendLine(FormatPriceLike(price.BidPrice));
        builder.Append("  ask_price: ").AppendLine(FormatPriceLike(price.AskPrice));
        builder.Append("  bid_size: ").AppendLine(FormatQuantityOrValue(price.BidSize));
        builder.Append("  ask_size: ").AppendLine(FormatQuantityOrValue(price.AskSize));
        builder.Append("  spread_abs: ").AppendLine(FormatPriceLike(price.SpreadAbs));
        builder.Append("  spread_pct: ").AppendLine(FormatPercent(price.SpreadPct));
        builder.Append("  price_24h_change_pct: ").AppendLine(FormatPercent(price.Price24hChangePct));
        builder.Append("  high_24h: ").AppendLine(FormatPriceLike(price.High24h));
        builder.Append("  low_24h: ").AppendLine(FormatPriceLike(price.Low24h));
        builder.Append("  volume_24h: ").AppendLine(FormatQuantityOrValue(price.Volume24h));
        builder.Append("  turnover_24h: ").AppendLine(FormatQuantityOrValue(price.Turnover24h));
        builder.AppendLine();
    }

    private static void AppendDerivativesSection(StringBuilder builder, DerivativesSnapshot derivatives)
    {
        builder.AppendLine("derivatives:");
        builder.Append("  funding_rate: ").AppendLine(FormatMetricOrRatio(derivatives.FundingRate));
        builder.Append("  funding_rate_avg_24h: ").AppendLine(FormatMetricOrRatio(derivatives.FundingRateAvg24h));
        builder.Append("  next_funding_time_utc: ").AppendLine(FormatDateTime(derivatives.NextFundingTimeUtc));
        builder.Append("  open_interest: ").AppendLine(FormatQuantityOrValue(derivatives.OpenInterest));
        builder.Append("  open_interest_value: ").AppendLine(FormatQuantityOrValue(derivatives.OpenInterestValue));
        builder.Append("  open_interest_change_1h_pct: ").AppendLine(FormatPercent(derivatives.OpenInterestChange1hPct));
        builder.Append("  open_interest_change_4h_pct: ").AppendLine(FormatPercent(derivatives.OpenInterestChange4hPct));
        builder.Append("  premium_vs_index_pct: ").AppendLine(FormatPercent(derivatives.PremiumVsIndexPct));
        builder.Append("  long_ratio: ").AppendLine(FormatMetricOrRatio(derivatives.LongRatio));
        builder.Append("  short_ratio: ").AppendLine(FormatMetricOrRatio(derivatives.ShortRatio));
        builder.AppendLine();
    }

    private static void AppendOrderBookSection(StringBuilder builder, OrderBookSnapshot orderBook)
    {
        builder.AppendLine("order_book:");
        builder.Append("  captured_at_utc: ").AppendLine(orderBook.CapturedAtUtc.ToString("O", _invariantCulture));
        builder.Append("  best_bid_price: ").AppendLine(FormatPriceLike(orderBook.BestBidPrice));
        builder.Append("  best_ask_price: ").AppendLine(FormatPriceLike(orderBook.BestAskPrice));
        builder.Append("  total_bid_volume_top5: ").AppendLine(FormatQuantityOrValue(orderBook.TotalBidVolumeTop5));
        builder.Append("  total_ask_volume_top5: ").AppendLine(FormatQuantityOrValue(orderBook.TotalAskVolumeTop5));
        builder.Append("  total_bid_volume_top10: ").AppendLine(FormatQuantityOrValue(orderBook.TotalBidVolumeTop10));
        builder.Append("  total_ask_volume_top10: ").AppendLine(FormatQuantityOrValue(orderBook.TotalAskVolumeTop10));
        builder.Append("  total_bid_volume_top20: ").AppendLine(FormatQuantityOrValue(orderBook.TotalBidVolumeTop20));
        builder.Append("  total_ask_volume_top20: ").AppendLine(FormatQuantityOrValue(orderBook.TotalAskVolumeTop20));
        builder.Append("  imbalance_top5: ").AppendLine(FormatMetricOrRatio(orderBook.ImbalanceTop5));
        builder.Append("  imbalance_top10: ").AppendLine(FormatMetricOrRatio(orderBook.ImbalanceTop10));
        builder.Append("  imbalance_top20: ").AppendLine(FormatMetricOrRatio(orderBook.ImbalanceTop20));
        builder.Append("  bid_walls: ").AppendLine(FormatLiquidityWalls(orderBook.BidWalls));
        builder.Append("  ask_walls: ").AppendLine(FormatLiquidityWalls(orderBook.AskWalls));
        builder.AppendLine();
    }

    private static void AppendTradeFlowSection(StringBuilder builder, TradeFlowSnapshot tradeFlow)
    {
        builder.AppendLine("trade_flow:");
        builder.Append("  window_start_utc: ").AppendLine(tradeFlow.WindowStartUtc.ToString("O", _invariantCulture));
        builder.Append("  window_end_utc: ").AppendLine(tradeFlow.WindowEndUtc.ToString("O", _invariantCulture));
        builder.Append("  buy_volume: ").AppendLine(FormatQuantityOrValue(tradeFlow.BuyVolume));
        builder.Append("  sell_volume: ").AppendLine(FormatQuantityOrValue(tradeFlow.SellVolume));
        builder.Append("  delta_volume: ").AppendLine(FormatQuantityOrValue(tradeFlow.DeltaVolume));
        builder.Append("  delta_pct: ").AppendLine(FormatPercent(tradeFlow.DeltaPct));
        builder.Append("  total_trades: ").AppendLine(tradeFlow.TotalTrades.ToString(_invariantCulture));
        builder.Append("  buy_trades: ").AppendLine(tradeFlow.BuyTrades.ToString(_invariantCulture));
        builder.Append("  sell_trades: ").AppendLine(tradeFlow.SellTrades.ToString(_invariantCulture));
        builder.Append("  avg_trade_size: ").AppendLine(FormatQuantityOrValue(tradeFlow.AvgTradeSize));
        builder.Append("  max_trade_size: ").AppendLine(FormatQuantityOrValue(tradeFlow.MaxTradeSize));
        builder.Append("  aggressive_buy_pressure: ").AppendLine(FormatBoolean(tradeFlow.HasAggressiveBuyPressure));
        builder.Append("  aggressive_sell_pressure: ").AppendLine(FormatBoolean(tradeFlow.HasAggressiveSellPressure));
        builder.AppendLine();
    }

    private static void AppendTrendSection(StringBuilder builder, MarketSnapshot snapshot)
    {
        builder.AppendLine("trend:");
        AppendTimeframeLine(builder, snapshot.M15);
        AppendTimeframeLine(builder, snapshot.H1);
        AppendTimeframeLine(builder, snapshot.H4);
        AppendTimeframeLine(builder, snapshot.D1);
        builder.AppendLine();
    }

    private static void AppendTimeframeLine(StringBuilder builder, TimeframeAnalysisSnapshot timeframe)
    {
        builder.Append("  ")
            .Append(timeframe.Timeframe)
            .Append(": trend=")
            .Append(timeframe.Trend)
            .Append(", strength=")
            .Append(FormatMetricOrRatio(timeframe.TrendStrengthScore))
            .Append(", rsi14=")
            .Append(FormatMetricOrRatio(timeframe.Rsi14))
            .Append(", atr14=")
            .Append(FormatPriceLike(timeframe.Atr14))
            .Append(", volume_ratio=")
            .Append(FormatMetricOrRatio(timeframe.VolumeRatio))
            .Append(", ema20=")
            .Append(FormatPriceLike(timeframe.Ema20))
            .Append(", ema50=")
            .Append(FormatPriceLike(timeframe.Ema50))
            .Append(", ema200=")
            .Append(FormatPriceLike(timeframe.Ema200))
            .Append(", ema_alignment=")
            .Append(FormatEmaAlignment(timeframe))
            .Append(", last_close=")
            .Append(FormatPriceLike(timeframe.LastCandle.Close))
            .Append(", candle_open_time_utc=")
            .Append(timeframe.LastCandleOpenTimeUtc.ToString("O", _invariantCulture))
            .Append(", support1=")
            .Append(FormatPriceLike(timeframe.Support1))
            .Append(", support2=")
            .Append(FormatPriceLike(timeframe.Support2))
            .Append(", resistance1=")
            .Append(FormatPriceLike(timeframe.Resistance1))
            .Append(", resistance2=")
            .Append(FormatPriceLike(timeframe.Resistance2))
            .Append(", distance_to_support1_pct=")
            .Append(FormatPercent(timeframe.DistanceToSupport1Pct))
            .Append(", distance_to_resistance1_pct=")
            .Append(FormatPercent(timeframe.DistanceToResistance1Pct))
            .Append(", rsi_overbought=")
            .Append(FormatBoolean(timeframe.RsiOverbought))
            .Append(", rsi_oversold=")
            .Append(FormatBoolean(timeframe.RsiOversold))
            .Append(", rsi14_is_reliable=")
            .Append(FormatBoolean(timeframe.Rsi14IsReliable))
            .Append(", candle_range_pct=")
            .Append(FormatPercent(timeframe.CandleRangePct))
            .AppendLine();
    }

    private static void AppendSentimentSection(StringBuilder builder, SentimentSnapshot sentiment)
    {
        builder.AppendLine("sentiment:");
        builder.Append("  market_regime: ").AppendLine(string.IsNullOrWhiteSpace(sentiment.MarketRegime) ? NotAvailable : sentiment.MarketRegime);
        builder.Append("  long_short_bias_score: ").AppendLine(FormatMetricOrRatio(sentiment.LongShortBiasScore));
        builder.Append("  funding_bias_score: ").AppendLine(FormatMetricOrRatio(sentiment.FundingBiasScore));
        builder.Append("  order_book_pressure_score: ").AppendLine(FormatMetricOrRatio(sentiment.OrderBookPressureScore));
        builder.Append("  trade_flow_pressure_score: ").AppendLine(FormatMetricOrRatio(sentiment.TradeFlowPressureScore));
        builder.AppendLine();
    }

    private static void AppendPortfolioSection(StringBuilder builder, PortfolioSnapshot portfolio)
    {
        builder.AppendLine("portfolio:");
        builder.Append("  total_equity_usd: ").AppendLine(FormatQuantityOrValue(portfolio.TotalEquityUsd));
        builder.Append("  available_balance_usd: ").AppendLine(FormatQuantityOrValue(portfolio.AvailableBalanceUsd));
        builder.Append("  total_wallet_balance_usd: ").AppendLine(FormatQuantityOrValue(portfolio.TotalWalletBalanceUsd));
        builder.Append("  total_unrealized_pnl_usd: ").AppendLine(FormatQuantityOrValue(portfolio.TotalUnrealizedPnlUsd));
        builder.Append("  open_positions: ").AppendLine(FormatOpenPositions(portfolio.OpenPositions));
    }

    private static string FormatTags(List<string> tags)
    {
        if (tags.Count == 0)
        {
            return "[]";
        }

        return "[" + string.Join(", ", tags.Where(static tag => !string.IsNullOrWhiteSpace(tag))) + "]";
    }

    private static string FormatLiquidityWalls(List<LiquidityWall> walls)
    {
        if (walls.Count == 0)
        {
            return None;
        }

        return string.Join(
            "; ",
            walls.Select(static wall =>
                FormattableString.Invariant($"price={FormatPriceLike(wall.Price)}, size={FormatQuantityOrValue(wall.Size)}, distance_pct={FormatPercent(wall.DistancePctFromMarket)}")));
    }

    private static string FormatOpenPositions(List<OpenPositionSnapshot> openPositions)
    {
        if (openPositions.Count == 0)
        {
            return None;
        }

        return string.Join(
            "; ",
            openPositions.Select(static position =>
                FormattableString.Invariant(
                    $"symbol={position.Symbol}, side={position.Side}, size={FormatQuantityOrValue(position.Size)}, avg_price={FormatPriceLike(position.AvgPrice)}, mark_price={FormatPriceLike(position.MarkPrice)}, break_even_price={FormatPriceLike(position.BreakEvenPrice)}, liquidation_price={FormatPriceLike(position.LiquidationPrice)}, position_value_usd={FormatQuantityOrValue(position.PositionValueUsd)}, leverage={FormatMetricOrRatio(position.Leverage)}, unrealized_pnl_usd={FormatQuantityOrValue(position.UnrealizedPnlUsd)}, unrealized_pnl_pct={FormatPercent(position.UnrealizedPnlPct)}")));
    }

    private static string FormatEmaAlignment(TimeframeAnalysisSnapshot timeframe) =>
        timeframe.EmaBullishAlignment
            ? "bullish"
            : timeframe.EmaBearishAlignment
                ? "bearish"
                : "mixed";

    private static string FormatDateTime(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToString("O", _invariantCulture)
            : NotAvailable;

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string FormatPercent(decimal value) => value.ToString("0.####", _invariantCulture) + "%";

    private static string FormatPercent(decimal? value) =>
        value.HasValue ? FormatPercent(value.Value) : NotAvailable;

    private static string FormatMetricOrRatio(decimal value) => value.ToString("0.####", _invariantCulture);

    private static string FormatMetricOrRatio(decimal? value) =>
        value.HasValue ? FormatMetricOrRatio(value.Value) : NotAvailable;

    private static string FormatQuantityOrValue(decimal value) => value.ToString("0.####", _invariantCulture);

    private static string FormatPriceLike(decimal value) => value.ToString("0.################", _invariantCulture);

    private static string FormatPriceLike(decimal? value) =>
        value.HasValue ? FormatPriceLike(value.Value) : NotAvailable;
}
