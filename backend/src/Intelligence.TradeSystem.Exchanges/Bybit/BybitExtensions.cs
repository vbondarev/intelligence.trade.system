using Intelligence.TradeSystem.Domain;
using BybitCategory = Bybit.Net.Enums.Category;
using BybitKlineInterval = Bybit.Net.Enums.KlineInterval;

namespace Intelligence.TradeSystem.Exchanges.Bybit;

internal static class BybitExtensions
{
    public static BybitCategory ToBybitCategory(this MarketCategory category) =>
        category switch
        {
            MarketCategory.Spot => BybitCategory.Spot,
            MarketCategory.Linear => BybitCategory.Linear,
            MarketCategory.Inverse => BybitCategory.Inverse,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    public static BybitKlineInterval ToBybitInterval(this KlineInterval interval) =>
        interval switch
        {
            KlineInterval.OneMinute => BybitKlineInterval.OneMinute,
            KlineInterval.ThreeMinutes => BybitKlineInterval.ThreeMinutes,
            KlineInterval.FiveMinutes => BybitKlineInterval.FiveMinutes,
            KlineInterval.FifteenMinutes => BybitKlineInterval.FifteenMinutes,
            KlineInterval.ThirtyMinutes => BybitKlineInterval.ThirtyMinutes,
            KlineInterval.OneHour => BybitKlineInterval.OneHour,
            KlineInterval.TwoHours => BybitKlineInterval.TwoHours,
            KlineInterval.FourHours => BybitKlineInterval.FourHours,
            KlineInterval.SixHours => BybitKlineInterval.SixHours,
            KlineInterval.TwelveHours => BybitKlineInterval.TwelveHours,
            KlineInterval.OneDay => BybitKlineInterval.OneDay,
            KlineInterval.OneWeek => BybitKlineInterval.OneWeek,
            KlineInterval.OneMonth => BybitKlineInterval.OneMonth,
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
        };
}
