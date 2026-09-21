using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Api.Serialization;

internal static class CandleIntervalV1Codec
{
    public static IReadOnlyList<string> AllWireValues { get; } =
    [
        "1m",
        "3m",
        "5m",
        "15m",
        "30m",
        "1h",
        "2h",
        "4h",
        "6h",
        "12h",
        "1d",
        "1w",
        "1mo",
    ];

    public static bool TryParse(string? value, out KlineInterval interval)
    {
        interval = value switch
        {
            "1m" => KlineInterval.OneMinute,
            "3m" => KlineInterval.ThreeMinutes,
            "5m" => KlineInterval.FiveMinutes,
            "15m" => KlineInterval.FifteenMinutes,
            "30m" => KlineInterval.ThirtyMinutes,
            "1h" => KlineInterval.OneHour,
            "2h" => KlineInterval.TwoHours,
            "4h" => KlineInterval.FourHours,
            "6h" => KlineInterval.SixHours,
            "12h" => KlineInterval.TwelveHours,
            "1d" => KlineInterval.OneDay,
            "1w" => KlineInterval.OneWeek,
            "1mo" => KlineInterval.OneMonth,
            _ => default,
        };

        return value is not null && AllWireValues.Contains(value, StringComparer.Ordinal);
    }

    public static string ToWireValue(KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => "1m",
        KlineInterval.ThreeMinutes => "3m",
        KlineInterval.FiveMinutes => "5m",
        KlineInterval.FifteenMinutes => "15m",
        KlineInterval.ThirtyMinutes => "30m",
        KlineInterval.OneHour => "1h",
        KlineInterval.TwoHours => "2h",
        KlineInterval.FourHours => "4h",
        KlineInterval.SixHours => "6h",
        KlineInterval.TwelveHours => "12h",
        KlineInterval.OneDay => "1d",
        KlineInterval.OneWeek => "1w",
        KlineInterval.OneMonth => "1mo",
        _ => throw new NotSupportedException(
            $"Kline interval '{interval}' is not mapped to a v1 wire contract."),
    };
}
