using Intelligence.TradeSystem.MarketIntelligence.Valuation;
using FluentAssertions;

namespace Intelligence.TradeSystem.MarketIntelligence.Tests.Valuation;

public sealed class LinearPositionIndicativeValueCalculatorTests
{
    [Fact]
    public void Calculates_indicative_value()
    {
        LinearPositionIndicativeValueCalculator.Calculate(0.25m, 40_000m)
            .Should()
            .Be(10_000m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Returns_null_for_non_positive_size(decimal size)
    {
        LinearPositionIndicativeValueCalculator.Calculate(size, 100m)
            .Should()
            .BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Returns_null_for_non_positive_mark_price(decimal markPrice)
    {
        LinearPositionIndicativeValueCalculator.Calculate(1m, markPrice)
            .Should()
            .BeNull();
    }

    [Fact]
    public void Returns_null_when_the_product_overflows_decimal()
    {
        LinearPositionIndicativeValueCalculator.Calculate(decimal.MaxValue, 2m)
            .Should()
            .BeNull();
    }

    [Fact]
    public void Repeated_calls_return_the_same_result()
    {
        var expected = LinearPositionIndicativeValueCalculator.Calculate(0.25m, 40_000m);

        for (var index = 0; index < 10; index++)
        {
            LinearPositionIndicativeValueCalculator.Calculate(0.25m, 40_000m)
                .Should()
                .Be(expected);
        }
    }
}
