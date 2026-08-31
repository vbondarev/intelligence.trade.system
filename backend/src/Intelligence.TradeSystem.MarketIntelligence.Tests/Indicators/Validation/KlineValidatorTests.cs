using FluentAssertions;
using Intelligence.TradeSystem.MarketIntelligence.Tests.Indicators.Helpers;
using Intelligence.TradeSystem.MarketIntelligence.Indicators.Validation;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Indicators.Validation;

public sealed class KlineValidatorTests
{
    // ───── Validate — valid cases ─────

    [Fact]
    public void Valid_Kline_Returns_IsValid_True()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 102m, volume: 1000m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeTrue();
        result.ViolationReason.Should().BeNull();
    }

    [Fact]
    public void High_Equal_To_Low_Is_Valid()
    {
        // Doji candle: High == Low == Open == Close
        var kline = KlineFactory.Create(open: 100m, high: 100m, low: 100m, close: 100m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Zero_Volume_Is_Valid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 100m, volume: 0m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Open_Equal_To_High_Is_Valid()
    {
        var kline = KlineFactory.Create(open: 105m, high: 105m, low: 95m, close: 100m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Close_Equal_To_Low_Is_Valid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 95m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeTrue();
    }

    // ───── Validate — High < Low ─────

    [Fact]
    public void High_Less_Than_Low_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m);

        var result = KlineValidator.Validate(kline, 3);

        result.IsValid.Should().BeFalse();
        result.KlineIndex.Should().Be(3);
        result.ViolationReason.Should().Contain("High").And.Contain("Low");
    }

    // ───── Validate — negative prices ─────

    [Fact]
    public void Negative_Open_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: -1m, high: 105m, low: -1m, close: 100m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
        result.ViolationReason.Should().Contain("Open").And.Contain("negative");
    }

    [Fact]
    public void Negative_High_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: -5m, high: -1m, low: -10m, close: -5m);

        var result = KlineValidator.Validate(kline, 0);

        // High < Low triggers first, but either way it must be invalid.
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_Low_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: -1m, close: 100m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
        result.ViolationReason.Should().Contain("Low").And.Contain("negative");
    }

    [Fact]
    public void Negative_Close_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: -5m, close: -1m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_Volume_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 100m, volume: -1m);

        var result = KlineValidator.Validate(kline, 7);

        result.IsValid.Should().BeFalse();
        result.KlineIndex.Should().Be(7);
        result.ViolationReason.Should().Contain("Volume").And.Contain("negative");
    }

    // ───── Validate — Open/Close outside [Low, High] ─────

    [Fact]
    public void Open_Above_High_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 110m, high: 105m, low: 95m, close: 100m);

        // High < Open but also High < Low boundary — detector catches High < Low first;
        // either way the candle is invalid.
        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Open_Below_Low_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 90m, high: 105m, low: 95m, close: 100m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
        result.ViolationReason.Should().Contain("Open").And.Contain("outside");
    }

    [Fact]
    public void Close_Above_High_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 110m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
        result.ViolationReason.Should().Contain("Close").And.Contain("outside");
    }

    [Fact]
    public void Close_Below_Low_Is_Invalid()
    {
        var kline = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 80m);

        var result = KlineValidator.Validate(kline, 0);

        result.IsValid.Should().BeFalse();
        result.ViolationReason.Should().Contain("Close").And.Contain("outside");
    }

    // ───── Validate — index is preserved ─────

    [Fact]
    public void Validate_Preserves_Provided_Index()
    {
        var kline = KlineFactory.Create(open: 100m, high: 80m, low: 95m, close: 100m);

        var result = KlineValidator.Validate(kline, 42);

        result.KlineIndex.Should().Be(42);
    }

    // ───── FilterValid ─────

    [Fact]
    public void FilterValid_Returns_All_When_All_Valid()
    {
        var klines = KlineFactory.CreateSeries(10);

        var valid = KlineValidator.FilterValid(klines, out var violations);

        valid.Should().HaveCount(10);
        violations.Should().BeEmpty();
    }

    [Fact]
    public void FilterValid_Removes_Invalid_Klines()
    {
        var klines = KlineFactory.CreateSeries(5).ToList();
        // Inject one invalid candle (High < Low) at index 2.
        klines[2] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m);

        var valid = KlineValidator.FilterValid(klines, out var violations);

        valid.Should().HaveCount(4, because: "one candle was invalid");
        violations.Should().HaveCount(1);
        violations[0].KlineIndex.Should().Be(2);
    }

    [Fact]
    public void FilterValid_Reports_Violation_For_Negative_Volume()
    {
        var klines = KlineFactory.CreateSeries(3).ToList();
        klines[1] = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 100m, volume: -50m);

        KlineValidator.FilterValid(klines, out var violations);

        violations.Should().HaveCount(1);
        violations[0].ViolationReason.Should().Contain("Volume");
    }

    [Fact]
    public void FilterValid_Violations_Do_Not_Contain_Valid_Klines()
    {
        var klines = KlineFactory.CreateSeries(10);

        KlineValidator.FilterValid(klines, out var violations);

        violations.Should().BeEmpty();
    }

    [Fact]
    public void FilterValid_Returns_Empty_When_All_Invalid()
    {
        var klines = new[]
        {
            KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m),  // High < Low
            KlineFactory.Create(open: 100m, high: 80m, low: 95m, close: 95m),  // High < Low
        };

        var valid = KlineValidator.FilterValid(klines, out var violations);

        valid.Should().BeEmpty();
        violations.Should().HaveCount(2);
    }

    [Fact]
    public void FilterValid_Multiple_Violations_All_Reported()
    {
        var klines = KlineFactory.CreateSeries(5).ToList();
        klines[0] = KlineFactory.Create(open: 100m, high: 90m, low: 95m, close: 95m);  // High < Low
        klines[4] = KlineFactory.Create(open: 100m, high: 105m, low: 95m, close: 110m); // Close > High

        var valid = KlineValidator.FilterValid(klines, out var violations);

        valid.Should().HaveCount(3);
        violations.Should().HaveCount(2);
        violations.Select(v => v.KlineIndex).Should().BeEquivalentTo([0, 4]);
    }
}
