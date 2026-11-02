using Intelligence.TradeSystem.Application.Realtime;
using Intelligence.TradeSystem.Application.Realtime.State;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Domain.Identity;

namespace Intelligence.TradeSystem.Application.Tests.Realtime;

public sealed class MarketValuationProjectionTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("USDT")]
    [InlineData("USDC")]
    public void Fresh_linear_mark_price_derives_value_in_the_confirmed_settlement_asset(string asset)
    {
        var valuation = Project(
            SettlementAsset.From(asset),
            size: 0.25m,
            CreatePrice(40_000m, ObservedAt),
            ObservedAt.AddSeconds(4.999));

        valuation.MarkPrice.Should().Be(40_000m);
        valuation.IndicativePositionValue.Should().Be(10_000m);
        valuation.SettlementAsset.Should().Be(SettlementAsset.From(asset));
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Fresh);
    }

    [Fact]
    public void Stale_mark_price_remains_available_but_is_not_reported_as_fresh()
    {
        var valuation = Project(
            SettlementAsset.From("USDT"),
            size: 0.5m,
            CreatePrice(20_000m, ObservedAt),
            ObservedAt.Add(MarketValuationProjection.MarkPriceFreshnessAfter));

        valuation.MarkPrice.Should().Be(20_000m);
        valuation.IndicativePositionValue.Should().Be(10_000m);
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Stale);
    }

    [Fact]
    public void Missing_price_is_unknown_and_does_not_invent_financial_values()
    {
        var valuation = Project(
            SettlementAsset.From("USDT"),
            size: 1m,
            marketPrice: null,
            ObservedAt);

        valuation.MarkPrice.Should().BeNull();
        valuation.IndicativePositionValue.Should().BeNull();
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Unknown);
    }

    [Fact]
    public void Unconfirmed_settlement_asset_does_not_produce_an_indicative_value()
    {
        var valuation = MarketValuationProjection.Project(
            ExchangeId.Bybit,
            MarketCategory.Linear,
            default,
            1m,
            PositionTrackingState.Active,
            CreatePrice(100m, ObservedAt),
            ObservedAt);

        valuation.MarkPrice.Should().BeNull();
        valuation.IndicativePositionValue.Should().BeNull();
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.NotApplicable);
    }

    [Fact]
    public void Closed_or_non_linear_positions_do_not_receive_a_live_valuation()
    {
        var price = CreatePrice(100m, ObservedAt);
        var closed = MarketValuationProjection.Project(
            ExchangeId.Bybit,
            MarketCategory.Linear,
            SettlementAsset.From("USDT"),
            1m,
            PositionTrackingState.Closed,
            price,
            ObservedAt);
        var inverse = MarketValuationProjection.Project(
            ExchangeId.Bybit,
            MarketCategory.Inverse,
            SettlementAsset.From("USDT"),
            1m,
            PositionTrackingState.Active,
            price,
            ObservedAt);

        closed.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.NotApplicable);
        closed.MarkPrice.Should().BeNull();
        inverse.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.NotApplicable);
        inverse.IndicativePositionValue.Should().BeNull();
    }

    [Fact]
    public void Unrepresentable_derived_value_is_partial_without_losing_mark_price()
    {
        var valuation = Project(
            SettlementAsset.From("USDC"),
            size: 2m,
            CreatePrice(decimal.MaxValue, ObservedAt),
            ObservedAt);

        valuation.MarkPrice.Should().Be(decimal.MaxValue);
        valuation.IndicativePositionValue.Should().BeNull();
        valuation.FreshnessStatus.Should().Be(MarketValuationFreshnessStatus.Partial);
    }

    private static MarketValuation Project(
        SettlementAsset settlementAsset,
        decimal size,
        MarketMarkPrice? marketPrice,
        DateTimeOffset calculatedAt) =>
        MarketValuationProjection.Project(
            ExchangeId.Bybit,
            MarketCategory.Linear,
            settlementAsset,
            size,
            PositionTrackingState.Active,
            marketPrice,
            calculatedAt);

    private static MarketMarkPrice CreatePrice(decimal price, DateTimeOffset observedAt) =>
        new()
        {
            Exchange = ExchangeId.Bybit,
            Category = MarketCategory.Linear,
            Symbol = InstrumentId.From("BTCUSDT"),
            MarkPrice = price,
            ObservedAt = observedAt,
            ReceivedAt = observedAt,
            Source = MarketMarkPriceSource.PublicTicker,
        };
}
