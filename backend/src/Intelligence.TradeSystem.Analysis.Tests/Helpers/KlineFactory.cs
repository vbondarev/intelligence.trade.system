using Intelligence.TradeSystem.Domain;

namespace Intelligence.TradeSystem.Analysis.Tests.Helpers;

/// <summary>Управляемый тренд для генерации серии свечей.</summary>
public enum SeriesTrend { Flat, Bullish, Bearish }

/// <summary>
/// Фабрика тестовых свечей с фиксированными детерминированными значениями.
/// </summary>
public static class KlineFactory
{
    private const string DefaultSymbol = "BTCUSDT";

    /// <summary>Создаёт одиночную свечу с явными параметрами.</summary>
    public static Kline Create(
        decimal open     = 100m,
        decimal high     = 105m,
        decimal low      = 95m,
        decimal close    = 100m,
        decimal volume   = 1_000m,
        decimal turnover = 100_000m,
        DateTime? startTime = null) =>
        new(DefaultSymbol,
            MarketCategory.Linear,
            KlineInterval.OneHour,
            startTime ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            open, high, low, close, volume, turnover);

    /// <summary>
    /// Генерирует серию свечей заданной длины с управляемым трендом.
    /// Каждая свеча сдвинута на 1 час относительно предыдущей.
    /// </summary>
    /// <param name="count">Количество свечей. Для EMA200 нужно ≥ 200.</param>
    /// <param name="trend">Направление движения цены.</param>
    /// <param name="startPrice">Начальная цена закрытия.</param>
    public static IReadOnlyList<Kline> CreateSeries(
        int count,
        SeriesTrend trend = SeriesTrend.Flat,
        decimal startPrice = 100m)
    {
        var klines    = new List<Kline>(count);
        var baseTime  = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var prevClose = startPrice;

        for (var i = 0; i < count; i++)
        {
            decimal step = trend switch
            {
                SeriesTrend.Bullish => 0.5m,
                SeriesTrend.Bearish => -0.5m,
                _                   => i % 2 == 0 ? 0.1m : -0.1m
            };

            var open  = prevClose;
            var close = prevClose + step;
            var high  = Math.Max(open, close) + 2m;
            var low   = Math.Min(open, close) - 1m;

            klines.Add(new Kline(
                DefaultSymbol,
                MarketCategory.Linear,
                KlineInterval.OneHour,
                baseTime.AddHours(i),
                open,
                high,
                low,
                close,
                Volume:   1_000m + i,
                Turnover: (1_000m + i) * Math.Abs(close)));

            prevClose = close;
        }

        return klines;
    }
}


