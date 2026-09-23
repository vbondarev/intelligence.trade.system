using Bybit.Net.Objects.Models.V5;
using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Snapshots;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using BybitAccountType = Bybit.Net.Enums.AccountType;
using BybitPositionSide = Bybit.Net.Enums.PositionSide;
using BybitPositionStatus = Bybit.Net.Enums.PositionStatus;
using BybitOrderSide = Bybit.Net.Enums.OrderSide;
using DomainPositionSide = Intelligence.TradeSystem.Domain.Snapshots.PositionSide;
using BybitPositionIdx = Bybit.Net.Enums.PositionIdx;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class ToDomainTypeMapperExtensionsTests
{
    [Fact]
    public void Maps_account_balance_and_assets()
    {
        var balance = new BybitBalance
        {
            AccountType = BybitAccountType.Contract,
            TotalEquity = 100m,
            TotalWalletBalance = 90m,
            TotalAvailableBalance = 80m,
            TotalPerpUnrealizedPnl = 10m,
            Assets =
            [
                new BybitAssetBalance
                {
                    Asset = "USDT",
                    Equity = 100m,
                    UsdValue = 100m,
                    WalletBalance = 90m,
                    Free = 80m,
                    Locked = 10m,
                    UnrealizedPnl = 5m,
                },
            ],
        };

        var mapped = balance.MapAccountBalance();

        mapped.AccountType.Should().Be(AccountType.Contract);
        mapped.TotalEquity.Should().Be(100m);
        mapped.TotalWalletBalance.Should().Be(90m);
        mapped.TotalAvailableBalance.Should().Be(80m);
        mapped.TotalPerpUnrealizedPnl.Should().Be(10m);
        mapped.Coins.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new CoinBalance("USDT", 100m, 100m, 90m, 80m, 10m, 5m));
    }

    [Fact]
    public void Maps_null_assets_to_empty_collection_and_unknown_account_to_unified()
    {
        var mapped = new BybitBalance { AccountType = (BybitAccountType)999 }.MapAccountBalance();

        mapped.AccountType.Should().Be(AccountType.Unified);
        mapped.Coins.Should().BeEmpty();
    }

    [Fact]
    public void Maps_open_position_fields_and_utc_timestamps()
    {
        var createTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updateTime = createTime.AddMinutes(1);
        var mapped = new BybitPosition
        {
            Symbol = "BTCUSDT",
            Side = BybitPositionSide.Buy,
            PositionStatus = BybitPositionStatus.Liquidation,
            Quantity = 1.5m,
            AveragePrice = 100m,
            PositionValue = 150m,
            Leverage = 5m,
            MarkPrice = 101m,
            BreakEvenPrice = 99m,
            LiquidationPrice = 50m,
            UnrealizedPnl = 2m,
            TakeProfit = 110m,
            StopLoss = 90m,
            TrailingStop = 3m,
            RiskId = 7,
            RiskLimitValue = 1000m,
            CreateTime = createTime,
            UpdateTime = updateTime,
            PositionIdx = (BybitPositionIdx)1,
        };

        var mappedPosition = mapped.MapOpenPosition(MarketCategory.Linear);

        mappedPosition.Should().BeEquivalentTo(new OpenPosition(
            "BTCUSDT",
            MarketCategory.Linear,
            DomainPositionSide.Long,
            Intelligence.TradeSystem.Domain.PositionStatus.Liquidation,
            1.5m,
            100m,
            150m,
            5m,
            101m,
            99m,
            50m,
            2m,
            110m,
            90m,
            3m,
            7,
            1000m,
            new DateTimeOffset(createTime),
            new DateTimeOffset(updateTime),
            1));
    }

    [Theory]
    [InlineData(BybitPositionSide.Buy, DomainPositionSide.Long)]
    [InlineData(BybitPositionSide.Sell, DomainPositionSide.Short)]
    [InlineData(null, DomainPositionSide.Unknown)]
    public void Maps_position_side(BybitPositionSide? source, DomainPositionSide expected) =>
        new BybitPosition { Side = source }.MapOpenPosition(MarketCategory.Linear).Side.Should().Be(expected);

    [Theory]
    [InlineData(BybitPositionStatus.Normal, Intelligence.TradeSystem.Domain.PositionStatus.Normal)]
    [InlineData(BybitPositionStatus.Liquidation, Intelligence.TradeSystem.Domain.PositionStatus.Liquidation)]
    [InlineData(BybitPositionStatus.AutoDeleverage, Intelligence.TradeSystem.Domain.PositionStatus.AutoDeleverage)]
    [InlineData(BybitPositionStatus.Inactive, Intelligence.TradeSystem.Domain.PositionStatus.Inactive)]
    [InlineData(null, Intelligence.TradeSystem.Domain.PositionStatus.Normal)]
    public void Maps_position_status(BybitPositionStatus? source, Intelligence.TradeSystem.Domain.PositionStatus expected) =>
        new BybitPosition { PositionStatus = source }.MapOpenPosition(MarketCategory.Linear).Status.Should().Be(expected);

    [Fact]
    public void Preserves_position_idx()
    {
        var mapped = new BybitPosition { PositionIdx = (BybitPositionIdx)2 }
            .MapOpenPosition(MarketCategory.Linear);

        mapped.PositionIdx.Should().Be(2);
    }

    [Fact]
    public void Maps_derivatives_history_entries()
    {
        var timestamp = DateTime.UtcNow;

        var ratio = new BybitLongShortRatio
        {
            Timestamp = timestamp,
            BuyRatio = 0.6m,
            SellRatio = 0.4m,
        }.MapLongShortRatioEntry("BTCUSDT", MarketCategory.Linear);
        var funding = new BybitFundingHistory
        {
            Timestamp = timestamp,
            FundingRate = 0.001m,
        }.MapFundingRateEntry("BTCUSDT", MarketCategory.Linear);
        var interest = new BybitOpenInterest
        {
            Timestamp = timestamp,
            OpenInterest = 123m,
        }.MapOpenInterestEntry("BTCUSDT", MarketCategory.Linear);

        ratio.Should().BeEquivalentTo(new LongShortRatioEntry("BTCUSDT", MarketCategory.Linear, timestamp, 0.6m, 0.4m));
        funding.Should().BeEquivalentTo(new FundingRateEntry("BTCUSDT", MarketCategory.Linear, timestamp, 0.001m));
        interest.Should().BeEquivalentTo(new OpenInterestEntry("BTCUSDT", MarketCategory.Linear, timestamp, 123m));
    }

    [Theory]
    [InlineData(BybitOrderSide.Buy, TradeSide.Buy)]
    [InlineData(BybitOrderSide.Sell, TradeSide.Sell)]
    public void Maps_trade(BybitOrderSide side, TradeSide expectedSide)
    {
        var timestamp = DateTime.UtcNow;
        var mapped = new BybitTradeHistory
        {
            Timestamp = timestamp,
            Side = side,
            Quantity = 2m,
            Price = 101m,
        }.MapTrade("BTCUSDT", MarketCategory.Spot);

        mapped.Should().BeEquivalentTo(new Trade("BTCUSDT", MarketCategory.Spot, timestamp, expectedSide, 2m, 101m));
    }

    [Fact]
    public void Maps_spot_ticker_and_book_data()
    {
        var ticker = new BybitSpotTicker
        {
            LastPrice = 100m,
            BestBidPrice = 99m,
            BestBidQuantity = 2m,
            BestAskPrice = 101m,
            BestAskQuantity = 3m,
            PriceChangePercentag24h = 0.1m,
            HighPrice24h = 110m,
            LowPrice24h = 90m,
            Volume24h = 1000m,
            Turnover24h = 100000m,
        }.MapSpotTicker("BTCUSDT");

        ticker.Should().BeEquivalentTo(new Ticker(
            "BTCUSDT", MarketCategory.Spot, 100m, 0m, 0m, 99m, 2m, 101m, 3m,
            0.1m, 110m, 90m, 1000m, 100000m));
    }

    [Fact]
    public void Maps_linear_ticker_and_kline()
    {
        var nextFunding = DateTime.UtcNow.AddHours(1);
        var ticker = new BybitLinearInverseTicker
        {
            LastPrice = 100m,
            MarkPrice = 101m,
            IndexPrice = 99m,
            BestBidPrice = 98m,
            BestBidQuantity = 2m,
            BestAskPrice = 102m,
            BestAskQuantity = 3m,
            PriceChangePercentage24h = 0.1m,
            HighPrice24h = 110m,
            LowPrice24h = 90m,
            Volume24h = 1000m,
            Turnover24h = 100000m,
            FundingRate = 0.001m,
            NextFundingTime = nextFunding,
            OpenInterest = 50m,
            OpenInterestValue = 5000m,
        }.MapLinearInverseTicker("BTCUSDT", MarketCategory.Linear);

        ticker.FundingRate.Should().Be(0.001m);
        ticker.NextFundingTimeUtc.Should().Be(new DateTimeOffset(nextFunding));
        ticker.OpenInterest.Should().Be(50m);
        ticker.OpenInterestValue.Should().Be(5000m);

        var kline = new BybitKline
        {
            StartTime = nextFunding,
            OpenPrice = 100m,
            HighPrice = 110m,
            LowPrice = 90m,
            ClosePrice = 105m,
            Volume = 20m,
            QuoteVolume = 2000m,
        }.MapKline("BTCUSDT", MarketCategory.Linear, KlineInterval.OneHour);

        kline.Should().BeEquivalentTo(new Kline(
            "BTCUSDT", MarketCategory.Linear, KlineInterval.OneHour, nextFunding,
            100m, 110m, 90m, 105m, 20m, 2000m));
    }
}
