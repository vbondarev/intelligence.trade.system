namespace Intelligence.TradeSystem.Domain.Tests.Identity;

public sealed class UserIdTests
{
    [Fact]
    public void Two_Instances_With_Same_Guid_Are_Equal()
    {
        var guid = Guid.NewGuid();

        var first = UserId.FromGuid(guid);
        var second = UserId.FromGuid(guid);

        first.Should().Be(second);
        (first == second).Should().BeTrue();
    }

    [Fact]
    public void Instances_With_Different_Guid_Are_Not_Equal()
    {
        var first = UserId.FromGuid(Guid.NewGuid());
        var second = UserId.FromGuid(Guid.NewGuid());

        first.Should().NotBe(second);
    }

    [Fact]
    public void New_Creates_Non_Empty_Value()
    {
        var id = UserId.New();

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void FromGuid_Rejects_Empty_Guid()
    {
        var act = () => UserId.FromGuid(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_Returns_Underlying_Guid_Representation()
    {
        var guid = Guid.NewGuid();
        var id = UserId.FromGuid(guid);

        id.ToString().Should().Be(guid.ToString());
    }
}
