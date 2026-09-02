using Bybit.Net.Objects.Models.V5;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using BybitOrderSide = Bybit.Net.Enums.OrderSide;
using BybitPositionSide = Bybit.Net.Enums.PositionSide;
using BybitPositionStatus = Bybit.Net.Enums.PositionStatus;

namespace Intelligence.TradeSystem.Exchanges.Bybit.Mapping;

internal static class ToDomainTypeMapperExtensions
{
    public static AccountBalance MapAccountBalance(this BybitBalance b) =>
        new(b.AccountType.MapAccountType(),
            b.TotalEquity,
            b.TotalWalletBalance,
            b.TotalAvailableBalance,
            b.TotalPerpUnrealizedPnl,
            b.Assets?
                .Select(balance => balance.MapCoinBalance())
                .ToList() ?? []);

    public static OpenPosition MapOpenPosition(this BybitPosition p, MarketCategory category) =>
        new(p.Symbol,
            category,
            p.Side.MapPositionSide(),
            p.PositionStatus.MapPositionStatus(),
            p.Quantity,
            p.AveragePrice,
            p.PositionValue,
            p.Leverage,
            p.MarkPrice,
            p.BreakEvenPrice,
            p.LiquidationPrice,
            p.UnrealizedPnl,
            p.TakeProfit,
            p.StopLoss,
            p.TrailingStop,
            p.RiskId,
            p.RiskLimitValue,
            p.CreateTime.HasValue ? new DateTimeOffset(p.CreateTime.Value, TimeSpan.Zero) : null,
            p.UpdateTime.HasValue ? new DateTimeOffset(p.UpdateTime.Value, TimeSpan.Zero) : null);

    public static LongShortRatioEntry MapLongShortRatioEntry(
        this BybitLongShortRatio e, string symbol, MarketCategory category) =>
        new(symbol, category, e.Timestamp, e.BuyRatio, e.SellRatio);

    public static FundingRateEntry MapFundingRateEntry(
        this BybitFundingHistory e, string symbol, MarketCategory category) =>
        new(symbol, category, e.Timestamp, e.FundingRate);

    public static OpenInterestEntry MapOpenInterestEntry(
        this BybitOpenInterest e, string symbol, MarketCategory category) =>
        new(symbol, category, e.Timestamp, e.OpenInterest);

    public static Trade MapTrade(this BybitTradeHistory t, string symbol, MarketCategory category) =>
        new(symbol,
            category,
            t.Timestamp,
            t.Side == BybitOrderSide.Buy ? TradeSide.Buy : TradeSide.Sell,
            t.Quantity,
            t.Price);

    public static Ticker MapSpotTicker(
        this BybitSpotTicker ticker,
        string symbol) =>
        new(symbol,
            MarketCategory.Spot,
            ticker.LastPrice,
            MarkPrice: 0m,
            IndexPrice: 0m,
            ticker.BestBidPrice ?? 0m,
            ticker.BestBidQuantity ?? 0m,
            ticker.BestAskPrice ?? 0m,
            ticker.BestAskQuantity ?? 0m,
            ticker.PriceChangePercentag24h,
            ticker.HighPrice24h,
            ticker.LowPrice24h,
            ticker.Volume24h,
            ticker.Turnover24h);

    public static OrderBook MapOrderBook(this BybitOrderbook ob, MarketCategory category) =>
        new(ob.Symbol,
            category,
            ob.Timestamp,
            ob.Bids.Select(e => new OrderBookEntry(e.Price, e.Quantity)).ToList(),
            ob.Asks.Select(e => new OrderBookEntry(e.Price, e.Quantity)).ToList());

    public static Kline MapKline(
        this BybitKline kline,
        string symbol,
        MarketCategory category,
        KlineInterval interval) =>
        new(symbol, category, interval,
            kline.StartTime,
            kline.OpenPrice,
            kline.HighPrice,
            kline.LowPrice,
            kline.ClosePrice,
            kline.Volume,
            kline.QuoteVolume);

    public static Ticker MapLinearInverseTicker(
        this BybitLinearInverseTicker ticker,
        string symbol,
        MarketCategory category) =>
        new(symbol,
            category,
            ticker.LastPrice,
            ticker.MarkPrice,
            ticker.IndexPrice,
            ticker.BestBidPrice ?? 0m,
            ticker.BestBidQuantity ?? 0m,
            ticker.BestAskPrice ?? 0m,
            ticker.BestAskQuantity ?? 0m,
            ticker.PriceChangePercentage24h,
            ticker.HighPrice24h,
            ticker.LowPrice24h,
            ticker.Volume24h,
            ticker.Turnover24h)
        {
            FundingRate = ticker.FundingRate,
            NextFundingTimeUtc = ticker.NextFundingTime.HasValue
                ? new DateTimeOffset(ticker.NextFundingTime.Value, TimeSpan.Zero)
                : null,
            OpenInterest = ticker.OpenInterest,
            OpenInterestValue = ticker.OpenInterestValue,
        };


    private static PositionSide MapPositionSide(this BybitPositionSide? side) =>
        side switch
        {
            BybitPositionSide.Buy => PositionSide.Long,
            BybitPositionSide.Sell => PositionSide.Short,
            _ => PositionSide.Unknown,
        };

    private static PositionStatus MapPositionStatus(this BybitPositionStatus? status) =>
        status switch
        {
            BybitPositionStatus.Normal => PositionStatus.Normal,
            BybitPositionStatus.Liquidation => PositionStatus.Liquidation,
            BybitPositionStatus.AutoDeleverage => PositionStatus.AutoDeleverage,
            BybitPositionStatus.Inactive => PositionStatus.Inactive,
            _ => PositionStatus.Normal,
        };

    private static AccountType MapAccountType(this BybitAccountType accountType) =>
        accountType switch
        {
            BybitAccountType.Unified => AccountType.Unified,
            BybitAccountType.Contract => AccountType.Contract,
            BybitAccountType.Spot => AccountType.Spot,
            _ => AccountType.Unified,
        };

    private static CoinBalance MapCoinBalance(this BybitAssetBalance a) =>
        new(a.Asset,
            a.Equity,
            a.UsdValue,
            a.WalletBalance,
            a.Free,
            a.Locked,
            a.UnrealizedPnl);
}
