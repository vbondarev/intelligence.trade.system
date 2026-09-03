namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class PositionIdTests
{
    [Fact]
    public void Two_Instances_With_Same_Guid_Are_Equal()
    {
        var guid = Guid.NewGuid();

        var first = PositionId.FromGuid(guid);
        var second = PositionId.FromGuid(guid);

        first.Should().Be(second);
    }

    [Fact]
    public void Instances_With_Different_Guid_Are_Not_Equal()
    {
        var first = PositionId.FromGuid(Guid.NewGuid());
        var second = PositionId.FromGuid(Guid.NewGuid());

        first.Should().NotBe(second);
    }

    [Fact]
    public void New_Creates_Non_Empty_Value()
    {
        var id = PositionId.New();

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void FromGuid_Rejects_Empty_Guid()
    {
        var act = () => PositionId.FromGuid(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_Returns_Underlying_Guid_Representation()
    {
        var guid = Guid.NewGuid();
        var id = PositionId.FromGuid(guid);

        id.ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void Reopening_The_Same_Instrument_And_Side_Produces_A_New_PositionId()
    {
        // BTCUSDT Short закрыт, а затем снова открыт — это разные позиции в домене,
        // даже если символ/сторона совпадают. PositionId не выводится из них.
        var closedShort = PositionId.New();
        var reopenedShort = PositionId.New();

        reopenedShort.Should().NotBe(closedShort);
    }
}
