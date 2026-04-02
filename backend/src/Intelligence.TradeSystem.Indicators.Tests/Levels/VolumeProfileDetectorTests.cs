using FluentAssertions;
using Intelligence.TradeSystem.Indicators.Levels;
using Intelligence.TradeSystem.Indicators.Tests.Helpers;

namespace Intelligence.TradeSystem.Indicators.Tests.Levels;

public sealed class VolumeProfileDetectorTests
{
    [Fact]
    public void Returns_All_Zeros_When_Empty_Array()
    {
        var result = VolumeProfileDetector.Detect([]);

        result.Support1.Should().Be(0m);
        result.Support2.Should().Be(0m);
        result.Resistance1.Should().Be(0m);
        result.Resistance2.Should().Be(0m);
    }

    [Fact]
    public void Returns_Identical_Levels_When_Range_Is_Zero()
    {
        // Все OHLC одинаковы → range == 0 → все уровни равны цене
        var kline  = KlineFactory.Create(open: 100m, high: 100m, low: 100m, close: 100m);
        var result = VolumeProfileDetector.Detect([kline]);

        result.Support1.Should().Be(100m);
        result.Support2.Should().Be(100m);
        result.Resistance1.Should().Be(100m);
        result.Resistance2.Should().Be(100m);
    }

    [Fact]
    public void Support_Levels_Are_Below_Current_Price()
    {
        var klines      = KlineFactory.CreateSeries(50).ToArray();
        var result      = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Support1 > 0m)
            result.Support1.Should().BeLessThan(currentPrice);

        if (result.Support2 > 0m)
            result.Support2.Should().BeLessThan(currentPrice);
    }

    [Fact]
    public void Resistance_Levels_Are_Above_Current_Price()
    {
        var klines       = KlineFactory.CreateSeries(50).ToArray();
        var result       = VolumeProfileDetector.Detect(klines);
        var currentPrice = klines[^1].Close;

        if (result.Resistance1 > 0m)
            result.Resistance1.Should().BeGreaterThan(currentPrice);

        if (result.Resistance2 > 0m)
            result.Resistance2.Should().BeGreaterThan(currentPrice);
    }

    [Fact]
    public void Support1_Is_Closer_To_Price_Than_Support2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Support1 > 0m && result.Support2 > 0m)
            result.Support1.Should().BeGreaterThan(result.Support2);
    }

    [Fact]
    public void Resistance1_Is_Closer_To_Price_Than_Resistance2()
    {
        var klines = KlineFactory.CreateSeries(50).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        if (result.Resistance1 > 0m && result.Resistance2 > 0m)
            result.Resistance1.Should().BeLessThan(result.Resistance2);
    }

    [Fact]
    public void High_Volume_Bucket_Is_Selected_As_Level()
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Зона A: низкие цены (~45–55), огромный объём → ожидаем поддержку здесь
        var lowZone = Enumerable.Range(0, 10).Select(i => KlineFactory.Create(
            open: 50m, high: 55m, low: 45m, close: 52m,
            volume: 1_000_000m,
            startTime: baseTime.AddHours(i))).ToArray();

        // Зона B: текущая цена (~95–105), минимальный объём
        var highZone = Enumerable.Range(0, 5).Select(i => KlineFactory.Create(
            open: 100m, high: 105m, low: 95m, close: 100m,
            volume: 10m,
            startTime: baseTime.AddHours(10 + i))).ToArray();

        var klines = lowZone.Concat(highZone).ToArray();
        var result = VolumeProfileDetector.Detect(klines);

        // Support1 или Support2 должна попасть в высокообъёмную зону [45, 55]
        var isInHighVolumeZone = (result.Support1 >= 45m && result.Support1 <= 55m)
                              || (result.Support2 >= 45m && result.Support2 <= 55m);

        isInHighVolumeZone.Should().BeTrue(
            because: "высокообъёмная ценовая зона (45–55) должна быть определена как уровень поддержки");
    }
}


