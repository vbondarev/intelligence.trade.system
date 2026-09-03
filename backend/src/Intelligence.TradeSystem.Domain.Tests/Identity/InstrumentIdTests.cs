namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class InstrumentIdTests
{
    [Fact]
    public void From_Creates_Instrument_For_Valid_Symbol()
    {
        var instrument = InstrumentId.From("BTCUSDT");

        instrument.Value.Should().Be("BTCUSDT");
    }

    [Fact]
    public void From_Trims_Surrounding_Whitespace()
    {
        var instrument = InstrumentId.From("  BTCUSDT  ");

        instrument.Value.Should().Be("BTCUSDT");
    }

    [Fact]
    public void From_Rejects_Empty_String()
    {
        var act = () => InstrumentId.From("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_Rejects_Whitespace_Only_String()
    {
        var act = () => InstrumentId.From("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void From_Rejects_Null()
    {
        var act = () => InstrumentId.From(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Instances_With_Same_Value_Are_Equal()
    {
        var first = InstrumentId.From("BTCUSDT");
        var second = InstrumentId.From("BTCUSDT");

        first.Should().Be(second);
    }

    [Fact]
    public void Instances_With_Different_Value_Are_Not_Equal()
    {
        var first = InstrumentId.From("BTCUSDT");
        var second = InstrumentId.From("ETHUSDT");

        first.Should().NotBe(second);
    }
}
