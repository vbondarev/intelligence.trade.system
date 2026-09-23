using FluentAssertions;
using Intelligence.TradeSystem.Domain;
using Intelligence.TradeSystem.Exchanges.Bybit.Mapping;
using BybitCategory = global::Bybit.Net.Enums.Category;
using BybitDataPeriod = global::Bybit.Net.Enums.DataPeriod;
using BybitKlineInterval = global::Bybit.Net.Enums.KlineInterval;
using BybitOpenInterestInterval = global::Bybit.Net.Enums.OpenInterestInterval;
using BybitAccountType = global::Bybit.Net.Enums.AccountType;
using DomainAccountType = Intelligence.TradeSystem.Domain.AccountType;
using DomainKlineInterval = Intelligence.TradeSystem.Domain.KlineInterval;
using DomainOpenInterestInterval = Intelligence.TradeSystem.Domain.OpenInterestInterval;
using DomainLongShortRatioPeriod = Intelligence.TradeSystem.Domain.LongShortRatioPeriod;

namespace Intelligence.TradeSystem.Exchanges.Tests;

public sealed class ToBybitTypeMapperExtensionsTests
{
    [Theory]
    [InlineData(MarketCategory.Spot, BybitCategory.Spot)]
    [InlineData(MarketCategory.Linear, BybitCategory.Linear)]
    [InlineData(MarketCategory.Inverse, BybitCategory.Inverse)]
    public void Maps_market_category(MarketCategory source, BybitCategory expected) =>
        source.ToBybitCategory().Should().Be(expected);

    [Theory]
    [InlineData(DomainKlineInterval.OneMinute, BybitKlineInterval.OneMinute)]
    [InlineData(DomainKlineInterval.ThreeMinutes, BybitKlineInterval.ThreeMinutes)]
    [InlineData(DomainKlineInterval.FiveMinutes, BybitKlineInterval.FiveMinutes)]
    [InlineData(DomainKlineInterval.FifteenMinutes, BybitKlineInterval.FifteenMinutes)]
    [InlineData(DomainKlineInterval.ThirtyMinutes, BybitKlineInterval.ThirtyMinutes)]
    [InlineData(DomainKlineInterval.OneHour, BybitKlineInterval.OneHour)]
    [InlineData(DomainKlineInterval.TwoHours, BybitKlineInterval.TwoHours)]
    [InlineData(DomainKlineInterval.FourHours, BybitKlineInterval.FourHours)]
    [InlineData(DomainKlineInterval.SixHours, BybitKlineInterval.SixHours)]
    [InlineData(DomainKlineInterval.TwelveHours, BybitKlineInterval.TwelveHours)]
    [InlineData(DomainKlineInterval.OneDay, BybitKlineInterval.OneDay)]
    [InlineData(DomainKlineInterval.OneWeek, BybitKlineInterval.OneWeek)]
    [InlineData(DomainKlineInterval.OneMonth, BybitKlineInterval.OneMonth)]
    public void Maps_kline_interval(
        DomainKlineInterval source,
        BybitKlineInterval expected) =>
        source.ToBybitInterval().Should().Be(expected);

    [Theory]
    [InlineData(DomainOpenInterestInterval.FiveMinutes, BybitOpenInterestInterval.FiveMinutes)]
    [InlineData(DomainOpenInterestInterval.FifteenMinutes, BybitOpenInterestInterval.FifteenMinutes)]
    [InlineData(DomainOpenInterestInterval.ThirtyMinutes, BybitOpenInterestInterval.ThirtyMinutes)]
    [InlineData(DomainOpenInterestInterval.OneHour, BybitOpenInterestInterval.OneHour)]
    [InlineData(DomainOpenInterestInterval.FourHours, BybitOpenInterestInterval.FourHours)]
    [InlineData(DomainOpenInterestInterval.OneDay, BybitOpenInterestInterval.OneDay)]
    public void Maps_open_interest_interval(
        DomainOpenInterestInterval source,
        BybitOpenInterestInterval expected) =>
        source.ToBybitOpenInterestInterval().Should().Be(expected);

    [Theory]
    [InlineData(DomainLongShortRatioPeriod.FiveMinutes, BybitDataPeriod.FiveMinutes)]
    [InlineData(DomainLongShortRatioPeriod.FifteenMinutes, BybitDataPeriod.FifteenMinutes)]
    [InlineData(DomainLongShortRatioPeriod.ThirtyMinutes, BybitDataPeriod.ThirtyMinutes)]
    [InlineData(DomainLongShortRatioPeriod.OneHour, BybitDataPeriod.OneHour)]
    [InlineData(DomainLongShortRatioPeriod.FourHours, BybitDataPeriod.FourHours)]
    [InlineData(DomainLongShortRatioPeriod.OneDay, BybitDataPeriod.OneDay)]
    public void Maps_long_short_ratio_period(
        DomainLongShortRatioPeriod source,
        BybitDataPeriod expected) =>
        source.ToBybitDataPeriod().Should().Be(expected);

    [Theory]
    [InlineData(DomainAccountType.Unified, BybitAccountType.Unified)]
    [InlineData(DomainAccountType.Contract, BybitAccountType.Contract)]
    [InlineData(DomainAccountType.Spot, BybitAccountType.Spot)]
    public void Maps_account_type(
        DomainAccountType source,
        BybitAccountType expected) =>
        source.ToBybitAccountType().Should().Be(expected);

    [Theory]
    [InlineData((MarketCategory)999)]
    public void Rejects_unsupported_market_category(MarketCategory source) =>
        FluentActions.Invoking(() => source.ToBybitCategory())
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData((DomainKlineInterval)999)]
    public void Rejects_unsupported_kline_interval(DomainKlineInterval source) =>
        FluentActions.Invoking(() => source.ToBybitInterval())
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData((DomainOpenInterestInterval)999)]
    public void Rejects_unsupported_open_interest_interval(DomainOpenInterestInterval source) =>
        FluentActions.Invoking(() => source.ToBybitOpenInterestInterval())
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData((DomainLongShortRatioPeriod)999)]
    public void Rejects_unsupported_long_short_ratio_period(DomainLongShortRatioPeriod source) =>
        FluentActions.Invoking(() => source.ToBybitDataPeriod())
            .Should().Throw<ArgumentOutOfRangeException>();

    [Theory]
    [InlineData((DomainAccountType)999)]
    public void Rejects_unsupported_account_type(DomainAccountType source) =>
        FluentActions.Invoking(() => source.ToBybitAccountType())
            .Should().Throw<ArgumentOutOfRangeException>();
}
